using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Rubik.Core;
using Rubik.Solver3x3;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// App logic: moves, scramble, solving, entering your own cube and the solution player.
/// The UI (<see cref="CubeUI"/>) only calls these methods and listens to <see cref="Changed"/>.
/// </summary>
public class CubeAppController : MonoBehaviour
{
    public CubeView view;
    public int scrambleLength = 20;
    [Range(0.03f, 1f)] public float scrambleSecondsPerTurn = 0.08f;
    [Tooltip("Frame rate cap used when the driver ignores VSync")]
    public int targetFrameRate = 60;
    [Tooltip("Mouse movement in pixels before a drag on a sticker turns its layer")]
    public float dragThreshold = 12f;

    private Task _tablesReady;
    private Task<List<Move>> _solveTask;
    private float _normalSpeed;
    private Vector2 _pressPosition;
    private bool _pressOverUi;
    private Collider _dragSticker;
    private string _statusKey;
    private object[] _statusArgs;

    public CubeEditor Editor { get; } = new CubeEditor();
    public bool IsEditing { get; private set; }
    public bool IsSolving => _solveTask != null;
    public CubeValidation EditValidation { get; private set; }

    /// <summary>Status line in the current language.</summary>
    public string Status
    {
        get
        {
            if (IsEditing)
            {
                // recomputed so that color names follow a language switch
                var (key, args) = CubeEditor.Describe(EditValidation);
                return args == null ? Loc.T(key) : Loc.T(key, args);
            }
            if (_statusKey == null) return "";
            return _statusArgs == null ? Loc.T(_statusKey) : Loc.T(_statusKey, _statusArgs);
        }
    }

    // ---- solution player
    private List<Move> _solution;
    private Cube _solutionStart;

    public bool HasSolution => _solution != null;
    /// <summary>Changes every time a new solution appears or the solution is cleared.</summary>
    public int SolutionVersion { get; private set; }
    public int SolutionLength => _solution?.Count ?? 0;
    /// <summary>How many solution moves have been made (including ones still animating).</summary>
    public int SolutionPosition { get; private set; }
    public bool IsPlaying { get; private set; }
    public string SolutionMove(int i) => Notation.Format(new[] { _solution[i] }, view.size);

    /// <summary>Speed of manual moves and the player, in moves per second.</summary>
    public float TurnsPerSecond
    {
        get => 1f / _normalSpeed;
        set
        {
            _normalSpeed = Mathf.Clamp(1f / value, 0.05f, 2f);
            if (!view.IsBusy || IsPlaying) view.secondsPerTurn = _normalSpeed;
            Changed?.Invoke();
        }
    }

    /// <summary>Mode, status or solution changed.</summary>
    public event Action Changed;

    private void Awake()
    {
        // VSync keeps the frame rate at the monitor refresh rate instead of thousands of FPS;
        // targetFrameRate is the fallback when the driver forces VSync off.
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = targetFrameRate;

        // in Awake because the UI reads the speed in its own Start
        _normalSpeed = view.secondsPerTurn;
        Loc.Changed += OnLanguageChanged;
    }

    private void OnDestroy() => Loc.Changed -= OnLanguageChanged;

    private void OnLanguageChanged() => Changed?.Invoke();

    private void Start()
    {
        _tablesReady = Task.Run(() =>
        {
            if (!PruningTables.IsInitialized) PruningTables.Initialize(new Cube(3));
        });

        // a left drag that starts on the cube turns a layer instead of orbiting the camera
        var orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
        if (orbit != null) orbit.capturesLeftDrag = p => !IsEditing && RaycastSticker(p, out _);
    }

    private void Update()
    {
        if (!view.IsBusy) view.secondsPerTurn = _normalSpeed;
        PollSolve();
        if (IsPlaying && !view.IsBusy)
        {
            if (SolutionPosition < SolutionLength) StepForward(keepPlaying: true);
            else { IsPlaying = false; Changed?.Invoke(); }
        }
        if (IsEditing) HandleEditInput();
        else
        {
            HandleKeyboard();
            HandleMouseTurns();
        }
    }

    // ---------------------------------------------------------------- player

    public void StepForward() => StepForward(keepPlaying: false);

    private void StepForward(bool keepPlaying)
    {
        if (!HasSolution || SolutionPosition >= SolutionLength) return;
        IsPlaying = keepPlaying;
        view.Enqueue(_solution[SolutionPosition++]);
        Changed?.Invoke();
    }

    public void StepBack()
    {
        if (!HasSolution || SolutionPosition == 0) return;
        IsPlaying = false;
        view.Enqueue(_solution[--SolutionPosition].Inverse());
        Changed?.Invoke();
    }

    /// <summary>Instantly sets the state "after the first <paramref name="position"/> solution moves".</summary>
    public void JumpTo(int position)
    {
        if (!HasSolution) return;
        position = Mathf.Clamp(position, 0, SolutionLength);
        var cube = _solutionStart.Clone();
        for (int i = 0; i < position; i++) cube.Apply(_solution[i]);
        view.SetState(cube);
        SolutionPosition = position;
        IsPlaying = false;
        Changed?.Invoke();
    }

    public void TogglePlay()
    {
        if (!HasSolution) return;
        if (IsPlaying) { IsPlaying = false; Changed?.Invoke(); return; }
        if (SolutionPosition >= SolutionLength) JumpTo(0);
        view.secondsPerTurn = _normalSpeed;
        IsPlaying = true;
        Changed?.Invoke();
    }

    private void ClearSolution()
    {
        if (_solution != null) SolutionVersion++;
        _solution = null;
        _solutionStart = null;
        SolutionPosition = 0;
        IsPlaying = false;
    }

    // ---------------------------------------------------------------- normal mode

    public void ResetCube()
    {
        _solveTask = null; // a solution for the old state is no longer needed
        ClearSolution();
        view.ResetCube();
        SetStatus("status.reset");
    }

    public void Scramble()
    {
        if (IsEditing || IsSolving) return;
        view.FinishAll();
        var scramble = Scrambler.Generate(view.size, scrambleLength, Environment.TickCount);
        view.secondsPerTurn = scrambleSecondsPerTurn;
        view.Enqueue(scramble);
        ClearSolution();
        SetStatus("status.scramble", Notation.Format(scramble, view.size));
    }

    public void Solve()
    {
        if (IsEditing || IsSolving) return;
        view.FinishAll();
        if (view.size != 3) { SetStatus("status.only3x3"); return; }
        if (view.Model.IsSolved) { SetStatus("status.alreadySolved"); return; }

        ClearSolution();
        var start = view.Model.Clone();
        _solutionStart = start.Clone();
        _solveTask = Task.Run(async () =>
        {
            await _tablesReady;
            return KociembaSolver.Solve(start.Clone()) ?? LayerByLayerSolver.Solve(start.Clone());
        });
        SetStatus("status.solving");
    }

    public void ChangeSpeed(float factor) => TurnsPerSecond *= factor;

    /// <summary>A move made by the user (keyboard or mouse); it invalidates the current solution.</summary>
    private void ManualMove(IEnumerable<Move> moves)
    {
        if (IsSolving) return;
        view.Enqueue(moves);
        if (HasSolution)
        {
            ClearSolution();
            SetStatus(null);
        }
    }

    private void PollSolve()
    {
        if (_solveTask == null || !_solveTask.IsCompleted) return;
        var task = _solveTask;
        _solveTask = null;

        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception);
            SetStatus("status.solverError");
            return;
        }
        _solution = task.Result;
        SolutionPosition = 0;
        SolutionVersion++;
        SetStatus("status.solution", _solution.Count);
    }

    private void HandleKeyboard()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.backspaceKey.wasPressedThisFrame) ResetCube();
        if (IsSolving) return;

        if (HasSolution)
        {
            if (kb.rightArrowKey.wasPressedThisFrame) StepForward();
            if (kb.leftArrowKey.wasPressedThisFrame) StepBack();
            if (kb.homeKey.wasPressedThisFrame) JumpTo(0);
            if (kb.endKey.wasPressedThisFrame) JumpTo(SolutionLength);
            if (kb.pKey.wasPressedThisFrame) TogglePlay();
        }

        // keyboard moves are queued even while an animation is running
        bool prime = kb.shiftKey.isPressed;
        TryMoveKey(kb.uKey, "U", prime);
        TryMoveKey(kb.rKey, "R", prime);
        TryMoveKey(kb.fKey, "F", prime);
        TryMoveKey(kb.dKey, "D", prime);
        TryMoveKey(kb.lKey, "L", prime);
        TryMoveKey(kb.bKey, "B", prime);
        TryMoveKey(kb.xKey, "x", prime);
        TryMoveKey(kb.yKey, "y", prime);
        TryMoveKey(kb.zKey, "z", prime);

        if (kb.spaceKey.wasPressedThisFrame) Scramble();
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Solve();
        if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) ChangeSpeed(1f / 0.7f);
        if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) ChangeSpeed(0.7f);
    }

    private void TryMoveKey(UnityEngine.InputSystem.Controls.KeyControl key, string move, bool prime)
    {
        if (key.wasPressedThisFrame) ManualMove(Notation.Parse(prime ? move + "'" : move, view.size));
    }

    /// <summary>Drag on a sticker of the 3D cube: once the mouse moves far enough, turn that layer.</summary>
    private void HandleMouseTurns()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;
        var position = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            _dragSticker = null;
            if (!CubeUI.IsPointerOverUi(position) && RaycastSticker(position, out var hit))
            {
                _dragSticker = hit.collider;
                _pressPosition = position;
            }
        }
        if (!mouse.leftButton.isPressed) _dragSticker = null;
        if (_dragSticker == null) return;

        var delta = position - _pressPosition;
        if (delta.magnitude < dragThreshold) return;

        if (view.TryGetDragMove(_dragSticker, delta, Camera.main, out var move))
            ManualMove(new[] { move });
        _dragSticker = null; // one move per drag
    }

    private bool RaycastSticker(Vector2 screenPosition, out RaycastHit hit)
    {
        hit = default;
        var cam = Camera.main;
        return cam != null &&
               Physics.Raycast(cam.ScreenPointToRay(screenPosition), out hit) &&
               view.TryGetStickerIndex(hit.collider, out _);
    }

    // ---------------------------------------------------------------- edit mode

    public void BeginEdit()
    {
        if (IsEditing || view.size != 3) return;
        _solveTask = null;
        ClearSolution();
        view.FinishAll();
        Editor.Load(view.Model);
        IsEditing = true;
        OnEditChanged();
    }

    public void ClearEdit()
    {
        if (!IsEditing) return;
        Editor.Clear();
        OnEditChanged();
    }

    public void SelectColor(byte color)
    {
        Editor.SelectedColor = color;
        Changed?.Invoke();
    }

    public void PaintSticker(int index)
    {
        if (IsEditing && Editor.Paint(index)) OnEditChanged();
    }

    public void ApplyEdit()
    {
        if (!IsEditing || !EditValidation.IsValid) return;
        IsEditing = false;
        view.SetState(Editor.ToCube());
        SetStatus("status.entered");
    }

    public void CancelEdit()
    {
        if (!IsEditing) return;
        IsEditing = false;
        view.ShowModel();
        SetStatus(null);
    }

    private void OnEditChanged()
    {
        EditValidation = Editor.Validate();
        view.ShowStickers(Editor.Stickers);
        Changed?.Invoke();
    }

    private void HandleEditInput()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            // 1..6 select a color
            var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key };
            for (int i = 0; i < digits.Length; i++)
                if (digits[i].wasPressedThisFrame) SelectColor((byte)i);
            if (kb.escapeKey.wasPressedThisFrame) CancelEdit();
        }

        // a click (without dragging) on a sticker of the 3D cube paints it
        var mouse = Mouse.current;
        if (mouse == null) return;
        var position = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame)
        {
            _pressPosition = position;
            _pressOverUi = CubeUI.IsPointerOverUi(position);
        }
        if (mouse.leftButton.wasReleasedThisFrame && !_pressOverUi && (position - _pressPosition).sqrMagnitude < 36f &&
            RaycastSticker(position, out var hit) && view.TryGetStickerIndex(hit.collider, out int index))
            PaintSticker(index);
    }

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args != null && args.Length > 0 ? args : null;
        Changed?.Invoke();
    }
}
