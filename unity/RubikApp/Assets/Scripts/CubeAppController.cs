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
    public string SolutionText { get; private set; } = "";

    /// <summary>Змінився режим, статус або рішення.</summary>
    public event Action Changed;

    private void Start()
    {
        _normalSpeed = view.secondsPerTurn;
        _tablesReady = Task.Run(() =>
        {
            if (!PruningTables.IsInitialized) PruningTables.Initialize(new Cube(3));
        });
    }

    private void Update()
    {
        if (!view.IsBusy) view.secondsPerTurn = _normalSpeed;
        PollSolve();
        if (IsEditing) HandleStickerClicks();
        else HandleKeyboard();
    }

    // ---------------------------------------------------------------- звичайний режим

    public void ResetCube()
    {
        _solveTask = null; // результат розв'язання для старого стану більше не потрібен
        view.ResetCube();
        SolutionText = "";
        SetStatus("Скинуто");
    }

    public void Scramble()
    {
        if (IsEditing || IsSolving || view.IsBusy) return;
        var scramble = Scrambler.Generate(view.size, scrambleLength, Environment.TickCount);
        view.secondsPerTurn = scrambleSecondsPerTurn;
        view.Enqueue(scramble);
        SolutionText = "";
        SetStatus("Скрамбл: " + Notation.Format(scramble, view.size));
    }

    public void Solve()
    {
        if (IsEditing || IsSolving || view.IsBusy) return;
        if (view.size != 3) { SetStatus("Солвер підтримує тільки 3x3"); return; }
        if (view.Model.IsSolved) { SetStatus("Куб уже зібраний"); return; }

        var start = view.Model.Clone();
        _solveTask = Task.Run(async () =>
        {
            await _tablesReady;
            return KociembaSolver.Solve(start.Clone()) ?? LayerByLayerSolver.Solve(start.Clone());
        });
        SetStatus("Шукаю рішення...");
    }

    public void ChangeSpeed(float factor)
    {
        _normalSpeed = Mathf.Clamp(_normalSpeed * factor, 0.03f, 1f);
        view.secondsPerTurn = _normalSpeed;
    }

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
        var solution = task.Result;
        SolutionText = Notation.Format(solution, view.size);
        view.Enqueue(solution);
        SetStatus($"Рішення: {solution.Count} ходів");
    }

    private void HandleKeyboard()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.backspaceKey.wasPressedThisFrame) ResetCube();
        if (IsSolving) return;

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
        if (key.wasPressedThisFrame) view.Enqueue(Notation.Parse(prime ? move + "'" : move, view.size));
    }

    // ---------------------------------------------------------------- режим введення

    public void BeginEdit()
    {
        if (IsEditing || view.size != 3) return;
        _solveTask = null;
        view.FinishAll();
        Editor.Load(view.Model);
        IsEditing = true;
        SolutionText = "";
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
