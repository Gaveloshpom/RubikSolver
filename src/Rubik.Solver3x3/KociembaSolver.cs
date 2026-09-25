using System;
using System.Collections.Generic;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    public static class KociembaSolver
    {
        // Фаза 1 використовує всі можливі повороти зовнішніх граней
        private static readonly string[] Phase1MoveNames = ConstrainedSearch.AllMoves;

        // Фаза 2 використовує обмежений набір (тільки подвійні повороти для R, L, F, B)
        private static readonly string[] Phase2MoveNames = { "U", "U'", "U2", "D", "D'", "D2", "R2", "L2", "F2", "B2" };

        public static List<Move> Solve(Cube start)
        {
            if (IsSolved(start)) return new List<Move>();

            var p1Moves = ParseMoves(Phase1MoveNames);
            var p2Moves = ParseMoves(Phase2MoveNames);

            var workCube = start.Clone();
            var fullSolution = new List<Move>();

            // ==========================================
            // ФАЗА 1: Приведення до підгрупи G1
            // ==========================================
            List<Move> phase1Solution = null;
            // Обмежуємо глибину Фази 1 (зазвичай вистачає 10-12 ходів)
            for (int depth = 0; depth <= 12; depth++)
            {
                var path = new List<Move>();
                if (SearchPhase1(workCube, 0, depth, path, p1Moves))
                {
                    phase1Solution = path;
                    break;
                }
            }

            if (phase1Solution == null) return null; // Якщо надто заплутано для нашого простого ліміту

            //workCube.Apply(phase1Solution);
            fullSolution.AddRange(phase1Solution);

            // ==========================================
            // ФАЗА 2: Повне вирішення з обмеженими ходами
            // ==========================================
            List<Move> phase2Solution = null;
            // Обмежуємо глибину Фази 2 (зазвичай вистачає 12-18 ходів)
            for (int depth = 0; depth <= 18; depth++)
            {
                var path = new List<Move>();
                if (SearchPhase2(workCube, 0, depth, path, p2Moves))
                {
                    phase2Solution = path;
                    break;
                }
            }

            if (phase2Solution == null) return null;

            fullSolution.AddRange(phase2Solution);
            return MoveOptimizer.Optimize(fullSolution); // Оптимізуємо стик між Фазою 1 і Фазою 2
        }

        private static bool SearchPhase1(Cube cube, int currentDepth, int maxDepth, List<Move> path, Move[] moves)
        {
            int co = PruningTables.CornerOrientation[PruningTables.GetCornerOrientationCoordinate(cube)];

            // Зчитуємо дві координати та об'єднуємо їх так само, як у таблиці
            int eo = PruningTables.GetEdgeOrientationCoordinate(cube);
            int ud = PruningTables.GetUDSliceCoordinate(cube);
            int combinedCoordinate = eo * 4096 + ud;

            int p1_edgeSlice = PruningTables.P1_EdgeSlice[combinedCoordinate];

            // Евристика: тепер ми беремо точну кількість ходів для ребер + зрізу ОДНОЧАСНО
            int h = Math.Max(co, p1_edgeSlice);

            if (currentDepth + h > maxDepth) return false;

            // Стан G1 досягнуто, коли всі 3 координати дорівнюють нулю 
            // (в об'єднаній таблиці це значення 0, а co == 0)
            if (h == 0 && currentDepth == maxDepth) return true;

            for (int i = 0; i < moves.Length; i++)
            {
                var move = moves[i];
                if (path.Count > 0)
                {
                    var last = path[path.Count - 1];
                    if (last.Axis == move.Axis && last.LayerFrom == move.LayerFrom) continue;
                }

                cube.Apply(new[] { move });
                path.Add(move);

                if (SearchPhase1(cube, currentDepth + 1, maxDepth, path, moves)) return true;

                path.RemoveAt(path.Count - 1);
                cube.Apply(new[] { new Move(move.Axis, move.LayerFrom, move.LayerTo, 4 - move.Turns) });
            }
            return false;
        }

        private static bool SearchPhase2(Cube cube, int currentDepth, int maxDepth, List<Move> path, Move[] moves)
        {
            // У Фазі 2 ми використовуємо перестановку кутів (CP) ТА ребер (EP) для відсікання
            int cp = PruningTables.CornerPermutation[PruningTables.GetCornerPermutationCoordinate(cube)];
            int ep = PruningTables.EdgePermutation[PruningTables.GetEdgePermutationCoordinate(cube)];

            int h = Math.Max(cp, ep); // Беремо найгірший варіант з двох

            if (currentDepth + h > maxDepth) return false;

            if (currentDepth == maxDepth) return IsSolved(cube);

            for (int i = 0; i < moves.Length; i++)
            {
                var move = moves[i];
                if (path.Count > 0)
                {
                    var last = path[path.Count - 1];
                    if (last.Axis == move.Axis && last.LayerFrom == move.LayerFrom) continue;
                }

                cube.Apply(new[] { move });
                path.Add(move);

                if (SearchPhase2(cube, currentDepth + 1, maxDepth, path, moves)) return true;

                path.RemoveAt(path.Count - 1);
                cube.Apply(new[] { new Move(move.Axis, move.LayerFrom, move.LayerTo, 4 - move.Turns) });
            }
            return false;
        }

        private static Move[] ParseMoves(string[] moveNames)
        {
            var parsed = new Move[moveNames.Length];
            for (int i = 0; i < moveNames.Length; i++)
                parsed[i] = Notation.Parse(moveNames[i], 3)[0];
            return parsed;
        }

        private static bool IsSolved(Cube c)
        {
            foreach (Face f in Enum.GetValues(typeof(Face)))
            {
                byte center = c[f, 1, 1];
                for (int r = 0; r < 3; r++)
                    for (int col = 0; col < 3; col++)
                        if (c[f, r, col] != center) return false;
            }
            return true;
        }
    }
}