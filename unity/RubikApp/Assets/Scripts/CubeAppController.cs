using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Rubik.Core;
using Rubik.Solver3x3;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Логіка застосунку: ходи, скрамбл, розв'язання і режим введення свого куба.
/// UI (<see cref="CubeUI"/>) лише викликає ці методи і слухає <see cref="Changed"/>.
/// </summary>
public class CubeAppController : MonoBehaviour
{
    public CubeView view;
    public int scrambleLength = 20;
    [Range(0.03f, 1f)] public float scrambleSecondsPerTurn = 0.08f;

    private Task _tablesReady;
    private Task<List<Move>> _solveTask;
    private float _normalSpeed;
    private Vector2 _pressPosition;
    private bool _pressOverUi;

    public CubeEditor Editor { get; } = new CubeEditor();
    public bool IsEditing { get; private set; }
    public bool IsSolving => _solveTask != null;
    public CubeValidation EditValidation { get; private set; }
    public string Status { get; private set; } = "";

    // ---- програвач рішення
    private List<Move> _solution;
    private Cube _solutionStart;

    public bool HasSolution => _solution != null;
    /// <summary>Змінюється щоразу, коли з'являється нове рішення або воно скидається.</summary>
    public int SolutionVersion { get; private set; }
    public int SolutionLength => _solution?.Count ?? 0;
    /// <summary>Скільки ходів рішення вже зроблено (з урахуванням тих, що ще анімуються).</summary>
    public int SolutionPosition { get; private set; }
    public bool IsPlaying { get; private set; }
    public string SolutionMove(int i) => Notation.Format(new[] { _solution[i] }, view.size);

    /// <summary>Швидкість звичайних ходів і програвача, ходів за секунду.</summary>
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

    /// <summary>Змінився режим, статус або рішення.</summary>
    public event Action Changed;

    private void Awake()
    {
        // в Awake, бо UI читає швидкість уже у своєму Start
        _normalSpeed = view.secondsPerTurn;
    }

    private void Start()
    {
        _tablesReady = Task.Run(() =>
        {
            if (!PruningTables.IsInitialized) PruningTables.Initialize(new Cube(3));
        });
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
        if (IsEditing) HandleStickerClicks();
        else HandleKeyboard();
    }

    // ---------------------------------------------------------------- програвач

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

    /// <summary>Миттєво ставить стан "після перших position ходів рішення".</summary>
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

    // ---------------------------------------------------------------- звичайний режим

    public void ResetCube()
    {
        _solveTask = null; // результат розв'язання для старого стану більше не потрібен
        ClearSolution();
        view.ResetCube();
        SetStatus("Скинуто");
    }

    public void Scramble()
    {
        if (IsEditing || IsSolving) return;
        view.FinishAll();
        var scramble = Scrambler.Generate(view.size, scrambleLength, Environment.TickCount);
        view.secondsPerTurn = scrambleSecondsPerTurn;
        view.Enqueue(scramble);
        ClearSolution();
        SetStatus("Скрамбл: " + Notation.Format(scramble, view.size));
    }

    public void Solve()
    {
        if (IsEditing || IsSolving) return;
        view.FinishAll();
        if (view.size != 3) { SetStatus("Солвер підтримує тільки 3x3"); return; }
        if (view.Model.IsSolved) { SetStatus("Куб уже зібраний"); return; }

        ClearSolution();
        var start = view.Model.Clone();
        _solutionStart = start.Clone();
        _solveTask = Task.Run(async () =>
        {
            await _tablesReady;
            return KociembaSolver.Solve(start.Clone()) ?? LayerByLayerSolver.Solve(start.Clone());
        });
        SetStatus("Шукаю рішення...");
    }

    public void ChangeSpeed(float factor) => TurnsPerSecond *= factor;

    private void PollSolve()
    {
        if (_solveTask == null || !_solveTask.IsCompleted) return;
        var task = _solveTask;
        _solveTask = null;

        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception);
            SetStatus("Помилка солвера (див. Console)");
            return;
        }
        _solution = task.Result;
        SolutionPosition = 0;
        SolutionVersion++;
        SetStatus($"Рішення: {_solution.Count} ходів. «Грати» або → — крок");
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

        // ходи з клавіатури стають у чергу навіть під час анімації
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
        if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) ChangeSpeed(0.7f);
        if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) ChangeSpeed(1f / 0.7f);
    }

    private void TryMoveKey(UnityEngine.InputSystem.Controls.KeyControl key, string move, bool prime)
    {
        if (!key.wasPressedThisFrame) return;
        view.Enqueue(Notation.Parse(prime ? move + "'" : move, view.size));
        if (HasSolution)
        {
            // ручний хід — рішення більше не відповідає кубу
            ClearSolution();
            SetStatus("");
        }
    }

    // ---------------------------------------------------------------- режим введення

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
        SetStatus("Куб введено — можна розв'язувати");
    }

    public void CancelEdit()
    {
        if (!IsEditing) return;
        IsEditing = false;
        view.ShowModel();
        SetStatus("");
    }

    private void OnEditChanged()
    {
        EditValidation = Editor.Validate();
        view.ShowStickers(Editor.Stickers);
        SetStatus(CubeEditor.Describe(EditValidation));
    }

    private void HandleStickerClicks()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            // 1..6 — вибір кольору
            var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key };
            for (int i = 0; i < digits.Length; i++)
                if (digits[i].wasPressedThisFrame) SelectColor((byte)i);
            if (kb.escapeKey.wasPressedThisFrame) CancelEdit();
        }

        // клік (без перетягування) по наліпці 3D-куба фарбує її
        var mouse = Mouse.current;
        if (mouse == null) return;
        var position = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame)
        {
            _pressPosition = position;
            _pressOverUi = CubeUI.IsPointerOverUi(position);
        }
        if (mouse.leftButton.wasReleasedThisFrame && !_pressOverUi && (position - _pressPosition).sqrMagnitude < 36f)
        {
            var cam = Camera.main;
            if (cam != null && Physics.Raycast(cam.ScreenPointToRay(position), out var hit) &&
                view.TryGetStickerIndex(hit.collider, out int index))
                PaintSticker(index);
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        Changed?.Invoke();
    }
}
