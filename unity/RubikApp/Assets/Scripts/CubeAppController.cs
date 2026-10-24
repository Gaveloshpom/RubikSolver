using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Rubik.Core;
using Rubik.Solver3x3;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Тимчасове керування для прототипу (крок 2): клавіші ходів, скрамбл, розв'язання.
/// Повноцінний UI з'явиться на кроці 4.
/// </summary>
public class CubeAppController : MonoBehaviour
{
    public CubeView view;
    public int scrambleLength = 20;
    [Range(0.03f, 1f)] public float scrambleSecondsPerTurn = 0.08f;

    private Task _tablesReady;
    private Task<List<Move>> _solveTask;
    private float _normalSpeed;
    private string _status = "";
    private string _lastSolution = "";

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

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.backspaceKey.wasPressedThisFrame)
        {
            _solveTask = null; // результат розв'язання для старого стану більше не потрібен
            view.ResetCube();
            _status = "Reset";
            _lastSolution = "";
        }
        if (_solveTask != null) return;

        // ходи з клавіатури стають у чергу навіть під час анімації
        bool prime = kb.shiftKey.isPressed;
        TryFaceKey(kb.uKey, "U", prime);
        TryFaceKey(kb.rKey, "R", prime);
        TryFaceKey(kb.fKey, "F", prime);
        TryFaceKey(kb.dKey, "D", prime);
        TryFaceKey(kb.lKey, "L", prime);
        TryFaceKey(kb.bKey, "B", prime);
        TryFaceKey(kb.xKey, "x", prime);
        TryFaceKey(kb.yKey, "y", prime);
        TryFaceKey(kb.zKey, "z", prime);

        if (!view.IsBusy && kb.spaceKey.wasPressedThisFrame) Scramble();
        if (!view.IsBusy && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) Solve();
        if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) ChangeSpeed(0.7f);
        if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) ChangeSpeed(1f / 0.7f);
    }

    private void TryFaceKey(UnityEngine.InputSystem.Controls.KeyControl key, string move, bool prime)
    {
        if (key.wasPressedThisFrame) view.Enqueue(Notation.Parse(prime ? move + "'" : move, view.size));
    }

    private void ChangeSpeed(float factor)
    {
        _normalSpeed = Mathf.Clamp(_normalSpeed * factor, 0.03f, 1f);
        view.secondsPerTurn = _normalSpeed;
    }

    private void Scramble()
    {
        var scramble = Scrambler.Generate(view.size, scrambleLength, Environment.TickCount);
        view.secondsPerTurn = scrambleSecondsPerTurn;
        view.Enqueue(scramble);
        _status = "Scramble: " + Notation.Format(scramble, view.size);
        _lastSolution = "";
    }

    private void Solve()
    {
        if (view.size != 3) { _status = "Solver supports 3x3 only"; return; }
        if (view.Model.IsSolved) { _status = "Already solved"; return; }

        var start = view.Model.Clone();
        _status = "Solving...";
        _solveTask = Task.Run(async () =>
        {
            await _tablesReady;
            return KociembaSolver.Solve(start.Clone()) ?? LayerByLayerSolver.Solve(start.Clone());
        });
    }

    private void PollSolve()
    {
        if (_solveTask == null || !_solveTask.IsCompleted) return;
        var task = _solveTask;
        _solveTask = null;

        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception);
            _status = "Solver error (see Console)";
            return;
        }
        var solution = task.Result;
        _lastSolution = Notation.Format(solution, view.size);
        _status = $"Solution: {solution.Count} moves";
        view.Enqueue(solution);
    }

    private void OnGUI()
    {
        GUI.skin.label.fontSize = 16;
        GUILayout.BeginArea(new Rect(16, 12, Screen.width - 32, 220));
        GUILayout.Label("U R F D L B / x y z — хід (Shift = проти годинникової)\n" +
                        "Space — скрамбл   Enter — розв'язати   Backspace — скинути   +/- — швидкість\n" +
                        "Миша: тягни — обертати камеру, коліщатко — зум");
        GUILayout.Label(_status);
        if (_lastSolution.Length > 0) GUILayout.Label(_lastSolution);
        GUILayout.EndArea();
    }
}
