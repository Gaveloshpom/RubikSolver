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
    [Tooltip("Scramble length for a 3x3; bigger cubes get more moves, see ScrambleLength")]
    public int scrambleLength = 20;
    [Tooltip("Up to this size scrambles are animated; bigger cubes are scrambled instantly")]
    public int animatedScrambleMaxSize = 7;
    [Range(0.03f, 1f)] public float scrambleSecondsPerTurn = 0.08f;
    [Tooltip("Frame rate cap used when the driver ignores VSync")]
    public int targetFrameRate = 60;
    [Tooltip("Mouse movement in pixels before a drag on a sticker turns its layer")]
    public float dragThreshold = 12f;
    [Tooltip("Seconds the short (two-phase) search may take before falling back to the layer-by-layer solver")]
    public float shortSearchSeconds = 4f;
    [Tooltip("Same limit in the browser, where the search blocks the page")]
    public float webShortSearchSeconds = 1.5f;

    private Task _tablesReady;
    private Task<List<Move>> _solveTask;
    private float _normalSpeed;
    private Vector2 _pressPosition;
    private bool _pressOverUi;
    private int _dragSticker = -1; // sticker index under the pointer when a drag started, -1 = none
    private const string SizePrefsKey = "cubeSize";

    /// <summary>Smallest and largest cube the app offers.</summary>
    public const int MinSize = 2, MaxSize = Cube.MaxSize;
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
#if UNITY_WEBGL && !UNITY_EDITOR
        // In the browser the frame rate follows requestAnimationFrame; -1 is the recommended value
        Application.targetFrameRate = -1;
#else
        // VSync keeps the frame rate at the monitor refresh rate instead of thousands of FPS;
        // targetFrameRate is the fallback when the driver forces VSync off.
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = targetFrameRate;
#endif

        // in Awake because the UI reads the speed in its own Start
        _normalSpeed = view.secondsPerTurn;
        Loc.Changed += OnLanguageChanged;
    }

    private void OnDestroy() => Loc.Changed -= OnLanguageChanged;

    private void OnLanguageChanged() => Changed?.Invoke();

    private void Start()
    {
        _tablesReady = RunWork<object>(() =>
        {
            if (!PruningTables.IsInitialized) PruningTables.Initialize(new Cube(3));
            return null;
        }, waitForTables: false);
        if (!_tablesReady.IsCompleted) SetStatus("status.loading");

        // a left drag that starts on the cube turns a layer instead of orbiting the camera
        var orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
        if (orbit != null) orbit.capturesLeftDrag = p => !IsEditing && PickSticker(p, out _);

        int savedSize = Mathf.Clamp(PlayerPrefs.GetInt(SizePrefsKey, view.size), MinSize, MaxSize);
        if (savedSize != view.size) view.SetState(new Cube(savedSize));
    }

    /// <summary>Only the 3x3 has a solver and the cube editor so far.</summary>
    public bool CanSolve => view.size == 3;

    /// <summary>Switches to a solved cube of another size (2..100).</summary>
    public void SetSize(int newSize)
    {
        newSize = Mathf.Clamp(newSize, MinSize, MaxSize);
        if (newSize == view.size) return;
        if (IsEditing) CancelEdit();
        _solveTask = null;
        ClearSolution();
        view.SetState(new Cube(newSize));
        PlayerPrefs.SetInt(SizePrefsKey, newSize);
        PlayerPrefs.Save();
        SetStatus("status.size", newSize);
    }

    /// <summary>
    /// Bigger cubes need more moves to look mixed: 20 for a 3x3 and about 10 per layer above that,
    /// capped so a 100x100 scramble stays quick to apply.
    /// </summary>
    private int ScrambleLength(int n) => n <= 3 ? scrambleLength : Mathf.Clamp(10 * n, 40, 500);

    /// <summary>
    /// Runs heavy solver work off the UI. On desktop that is a thread-pool task; the browser has no
    /// threads, so there it runs on the main thread a couple of frames later (after the status text
    /// has been drawn). Either way the result comes back as a Task polled from Update.
    /// </summary>
    private Task<T> RunWork<T>(Func<T> work, bool waitForTables = true)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var tcs = new TaskCompletionSource<T>();
        StartCoroutine(RunOnMainThread(work, waitForTables, tcs));
        return tcs.Task;
#else
        var tables = waitForTables ? _tablesReady : Task.CompletedTask;
        return Task.Run(async () =>
        {
            await tables;
            return work();
        });
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private System.Collections.IEnumerator RunOnMainThread<T>(Func<T> work, bool waitForTables, TaskCompletionSource<T> tcs)
    {
        if (waitForTables)
            while (!_tablesReady.IsCompleted) yield return null;
        yield return null;
        yield return null;
        try { tcs.SetResult(work()); }
        catch (Exception e) { tcs.SetException(e); }
    }
#endif

    private void Update()
    {
        if (_statusKey == "status.loading" && _tablesReady.IsCompleted) SetStatus(null);
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
        int n = view.size;
        // inner slices only exist from 4x4 up; for the 3x3 keep outer faces, which the solvers expect (fixed centres)
        var scramble = Scrambler.Generate(n, ScrambleLength(n), Environment.TickCount, innerLayers: n > 3);
        view.secondsPerTurn = scrambleSecondsPerTurn;
        view.Enqueue(scramble);
        if (n > animatedScrambleMaxSize) view.FinishAll(); // hundreds of moves: show the result at once
        ClearSolution();
        if (scramble.Count <= 30) SetStatus("status.scramble", Notation.Format(scramble, n));
        else SetStatus("status.scrambleLong", scramble.Count);
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
#if UNITY_WEBGL && !UNITY_EDITOR
        var limit = TimeSpan.FromSeconds(webShortSearchSeconds);
#else
        var limit = TimeSpan.FromSeconds(shortSearchSeconds);
#endif
        // The two-phase search gives shorter solutions but can take long on some scrambles;
        // the layer-by-layer solver is always fast
        _solveTask = RunWork(() => KociembaSolver.Solve(start.Clone(), limit) ?? LayerByLayerSolver.Solve(start.Clone()));
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

    /// <summary>Drag on a sticker of the 3D cube (mouse or finger): once it moves far enough, turn that layer.</summary>
    private void HandleMouseTurns()
    {
        var pointer = PointerInput.Current;
        if (pointer == null) return;
        if (PointerInput.ActiveTouches >= 2) { _dragSticker = -1; return; } // pinch zoom, not a turn
        var position = pointer.position.ReadValue();

        if (pointer.press.wasPressedThisFrame)
        {
            _dragSticker = -1;
            if (!CubeUI.IsPointerOverUi(position) && PickSticker(position, out int index))
            {
                _dragSticker = index;
                _pressPosition = position;
            }
        }
        if (!pointer.press.isPressed) _dragSticker = -1;
        if (_dragSticker < 0) return;

        var delta = position - _pressPosition;
        if (delta.magnitude < dragThreshold * ScreenScale) return;

        if (view.TryGetDragMove(_dragSticker, delta, Camera.main, out var move))
            ManualMove(new[] { move });
        _dragSticker = -1; // one move per drag
    }

    /// <summary>Sticker under a screen point (index as in Cube.Stickers).</summary>
    private bool PickSticker(Vector2 screenPosition, out int index)
    {
        index = -1;
        var cam = Camera.main;
        return cam != null && view.TryPickSticker(cam.ScreenPointToRay(screenPosition), out index);
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

        // a click or tap (without dragging) on a sticker of the 3D cube paints it
        var pointer = PointerInput.Current;
        if (pointer == null) return;
        var position = pointer.position.ReadValue();
        if (pointer.press.wasPressedThisFrame)
        {
            _pressPosition = position;
            _pressOverUi = CubeUI.IsPointerOverUi(position);
        }
        float tapRadius = 6f * ScreenScale;
        if (pointer.press.wasReleasedThisFrame && !_pressOverUi && (position - _pressPosition).sqrMagnitude < tapRadius * tapRadius &&
            PickSticker(position, out int index))
            PaintSticker(index);
    }

    /// <summary>Pixel thresholds are tuned for a 900 px tall screen; dense phone screens need bigger ones.</summary>
    private static float ScreenScale => Mathf.Max(1f, Screen.height / 900f);

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args != null && args.Length > 0 ? args : null;
        Changed?.Invoke();
    }
}
