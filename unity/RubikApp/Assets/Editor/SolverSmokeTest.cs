using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Rubik.Core;
using Rubik.Solver3x3;
using UnityEditor;
using Debug = UnityEngine.Debug;

// Перевірка, що солвер підключено: меню Rubik → Solver Smoke Test.
// Пошук іде у фоновому потоці, щоб редактор не зависав.
public static class SolverSmokeTest
{
    [MenuItem("Rubik/Solver Smoke Test")]
    private static void Run()
    {
        Debug.Log("[Rubik] Smoke test started...");
        Task.Run(() =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                if (!PruningTables.IsInitialized) PruningTables.Initialize(new Cube(3));
                var tablesMs = sw.ElapsedMilliseconds;

                int seed = Environment.TickCount;
                var scramble = Scrambler.Generate(3, 20, seed);
                var cube = new Cube(3);
                cube.Apply(scramble);

                sw.Restart();
                var solution = KociembaSolver.Solve(cube);
                string solver = "Kociemba";
                if (solution == null)
                {
                    solution = LayerByLayerSolver.Solve(cube);
                    solver = "LayerByLayer (fallback)";
                }
                var solveMs = sw.ElapsedMilliseconds;

                var check = cube.Clone();
                check.Apply(solution);

                Debug.Log($"[Rubik] Scramble (seed {seed}): {Notation.Format(scramble, 3)}\n" +
                          $"Solution via {solver}, {solution.Count} moves: {Notation.Format(solution, 3)}\n" +
                          $"Tables: {tablesMs} ms, solve: {solveMs} ms, verified solved: {check.IsSolved}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        });
    }
}
