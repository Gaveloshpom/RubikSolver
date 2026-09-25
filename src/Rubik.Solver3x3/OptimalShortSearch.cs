using System;
using System.Collections.Generic;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    public static class OptimalShortSearch
    {
        // Використовуємо всі можливі ходи зовнішніми гранями
        private static readonly string[] AllMoves = ConstrainedSearch.AllMoves;

        public static List<Move> Solve(Cube start, int maxDepth = 7)
        {
            if (IsSolved(start)) return new List<Move>();

            // Парсимо ходи один раз, щоб не витрачати ресурси всередині циклу
            var parsedMoves = new Move[AllMoves.Length];
            for (int i = 0; i < AllMoves.Length; i++)
            {
                parsedMoves[i] = Notation.Parse(AllMoves[i], 3)[0];
            }

            var path = new List<Move>();
            var workCube = start.Clone();

            // Поступово збільшуємо глибину (Iterative Deepening)
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                if (Search(workCube, 0, depth, path, parsedMoves))
                {
                    return path;
                }
            }

            return null; // Рішення не знайдено в межах maxDepth
        }

        private static bool Search(Cube cube, int currentDepth, int maxDepth, List<Move> path, Move[] allMoves)
        {
            // МАГІЯ ВІДСІКАННЯ (PRUNING) x2:
            int coIndex = PruningTables.GetCornerOrientationCoordinate(cube);
            int eoIndex = PruningTables.GetEdgeOrientationCoordinate(cube);

            // Беремо найбільшу необхідну кількість ходів з обох таблиць
            int minMovesRequired = Math.Max(
                PruningTables.CornerOrientation[coIndex],
                PruningTables.EdgeOrientation[eoIndex]
            );

            // Якщо нам не вистачає ліміту ходів, щоб вирішити хоча б орієнтацію - відкидаємо гілку
            if (currentDepth + minMovesRequired > maxDepth)
            {
                return false;
            }

            if (currentDepth == maxDepth)
            {
                return IsSolved(cube);
            }

            for (int i = 0; i < allMoves.Length; i++)
            {
                var move = allMoves[i];

                // Відсікання зайвих гілок (Pruning):
                // Не крутимо ту саму грань двічі поспіль (наприклад, R, а потім R2)
                if (path.Count > 0)
                {
                    var last = path[path.Count - 1];
                    if (last.Axis == move.Axis && last.LayerFrom == move.LayerFrom)
                        continue;
                }

                // Застосовуємо хід (без клонування куба - економимо пам'ять і час)
                cube.Apply(new[] { move });
                path.Add(move);

                // Заглиблюємось далі
                if (Search(cube, currentDepth + 1, maxDepth, path, allMoves))
                {
                    return true;
                }

                // Відкат ходу (Backtracking): застосовуємо обернений рух
                path.RemoveAt(path.Count - 1);
                var inverse = new Move(move.Axis, move.LayerFrom, move.LayerTo, 4 - move.Turns);
                cube.Apply(new[] { inverse });
            }

            return false;
        }

        private static bool IsSolved(Cube c)
        {
            // Перевіряємо, чи всі грані мають однорідний колір
            foreach (Face f in Enum.GetValues(typeof(Face)))
            {
                byte centerColor = c[f, 1, 1];
                for (int r = 0; r < 3; r++)
                    for (int col = 0; col < 3; col++)
                        if (c[f, r, col] != centerColor) return false;
            }
            return true;
        }

        public static int GetCornerOrientationCoordinate(Cube c)
        {
            int coordinate = 0;
            // Беремо перші 7 кутів. Ваш PieceMap містить 8 слотів[cite: 4].
            // Для кожного слоту знаходимо його поточну орієнтацію (0, 1 або 2)
            for (int i = 0; i < 7; i++)
            {
                CornerSlot slot = (CornerSlot)i;
                var faces = PieceMap.CornerFacelets[i]; //[cite: 4]

                // Знаходимо кут за базовими кольорами і дивимось на його PrimaryIndex[cite: 3]
                byte a = c[faces[0].face, faces[0].row, faces[0].col];
                byte b = c[faces[1].face, faces[1].row, faces[1].col];
                byte d = c[faces[2].face, faces[2].row, faces[2].col];

                var loc = PieceFinder.FindCorner(c, a, b, d); //[cite: 3]
                coordinate = coordinate * 3 + loc.PrimaryIndex; //[cite: 3]
            }
            return coordinate;
        }


    }
}