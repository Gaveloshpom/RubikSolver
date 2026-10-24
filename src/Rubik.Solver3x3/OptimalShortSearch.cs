using System;
using System.Collections.Generic;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    /// <summary>
    /// IDA* пошук найкоротшого рішення для коротких скрамблів (до maxDepth ходів).
    ///
    /// Пошук іде по координатах орієнтації кутів (CO) та ребер (EO) через власні таблиці переходів,
    /// а справжній куб збирається й перевіряється тільки в листках, де CO і EO вже нульові. Таблиці
    /// будуються тут самі (ліниво, один раз) і не залежать від <see cref="PruningTables.Initialize"/>:
    /// раніше відсікання брало значення з PruningTables, які без Initialize (а EdgeOrientation —
    /// взагалі завжди) були нулями, тож пошук на глибину 7 перебирав ~10^8 вузлів на кубі з
    /// наліпками і "зависав" на кожному скрамблі, що не розв'язується за 7 ходів.
    /// </summary>
    public static class OptimalShortSearch
    {
        // Використовуємо всі можливі ходи зовнішніми гранями
        private static readonly string[] AllMoves = ConstrainedSearch.AllMoves;

        private const int CoCount = 6561;   // 3^8: орієнтація всіх 8 кутів
        private const int EoCount = 4096;   // 2^12: орієнтація всіх 12 ребер
        private const byte Unreachable = 255;

        private static readonly Move[] Moves;
        private static readonly int[,] CoMove = new int[CoCount, 18];
        private static readonly int[,] EoMove = new int[EoCount, 18];
        private static readonly byte[] CoDist = new byte[CoCount];
        private static readonly byte[] EoDist = new byte[EoCount];

        // Статичний конструктор CLR виконує рівно один раз і потокобезпечно (Unity шукає у фоні).
        static OptimalShortSearch()
        {
            Moves = new Move[AllMoves.Length];
            for (int i = 0; i < AllMoves.Length; i++)
                Moves[i] = Notation.Parse(AllMoves[i], 3)[0];

            BuildTables(CoMove, CoDist, CoCount, CornerOrientation);
            BuildTables(EoMove, EoDist, EoCount, EdgeOrientation);
        }

        public static List<Move> Solve(Cube start, int maxDepth = 7)
        {
            if (IsSolved(start)) return new List<Move>();

            int co = CornerOrientation(start), eo = EdgeOrientation(start);
            // Недосяжна орієнтація (перекручений кут / перевернуте ребро) — куб не збирається взагалі.
            if (CoDist[co] == Unreachable || EoDist[eo] == Unreachable) return null;

            var path = new int[maxDepth];

            // Поступово збільшуємо глибину (Iterative Deepening)
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                if (Search(start, co, eo, 0, depth, path))
                {
                    var result = new List<Move>(depth);
                    for (int i = 0; i < depth; i++) result.Add(Moves[path[i]]);
                    return result;
                }
            }

            return null; // Рішення не знайдено в межах maxDepth
        }

        private static bool Search(Cube start, int co, int eo, int currentDepth, int maxDepth, int[] path)
        {
            // Відсікання: стільки ходів потрібно хоча б для орієнтації кутів / ребер
            int remaining = maxDepth - currentDepth;
            if (CoDist[co] > remaining || EoDist[eo] > remaining) return false;

            if (remaining == 0)
            {
                // Орієнтація вже правильна (інакше відсікли б вище) — перевіряємо справжній куб.
                var check = start.Clone();
                for (int i = 0; i < maxDepth; i++) check.Apply(new[] { Moves[path[i]] });
                return IsSolved(check);
            }

            for (int i = 0; i < Moves.Length; i++)
            {
                var move = Moves[i];

                if (currentDepth > 0)
                {
                    var last = Moves[path[currentDepth - 1]];
                    if (last.Axis == move.Axis)
                    {
                        // Ту саму грань двічі поспіль не крутимо (R, потім R2 — це один хід),
                        // а протилежні грані комутують (L R == R L) — лишаємо тільки один порядок.
                        if (move.LayerFrom <= last.LayerFrom) continue;
                    }
                }

                path[currentDepth] = i;
                if (Search(start, CoMove[co, i], EoMove[eo, i], currentDepth + 1, maxDepth, path))
                    return true;
            }

            return false;
        }

        /// <summary>BFS від зібраного куба по графу координати: заповнює таблицю переходів і
        /// відстань до нуля. Для кожного значення координати зберігається один куб-представник —
        /// орієнтація після ходу не залежить від перестановки, тож одного представника досить.</summary>
        private static void BuildTables(int[,] move, byte[] dist, int count, Func<Cube, int> coord)
        {
            for (int i = 0; i < count; i++)
            {
                dist[i] = Unreachable;
                for (int m = 0; m < Moves.Length; m++) move[i, m] = -1;
            }

            var solved = new Cube(3);
            int startCoord = coord(solved);
            dist[startCoord] = 0;
            var queue = new Queue<(Cube cube, int coord)>();
            queue.Enqueue((solved, startCoord));

            while (queue.Count > 0)
            {
                var (cube, c) = queue.Dequeue();
                for (int m = 0; m < Moves.Length; m++)
                {
                    var next = cube.Clone();
                    next.Apply(new[] { Moves[m] });
                    int n = coord(next);
                    move[c, m] = n;
                    if (dist[n] == Unreachable)
                    {
                        dist[n] = (byte)(dist[c] + 1);
                        queue.Enqueue((next, n));
                    }
                }
            }
        }

        /// <summary>Для кожного з 8 кутів: на якій його наліпці (0, 1, 2) колір центру U або D.</summary>
        private static int CornerOrientation(Cube c)
        {
            byte u = c[Face.U, 1, 1], d = c[Face.D, 1, 1];
            int coordinate = 0;
            for (int i = 0; i < 8; i++)
            {
                var f = PieceMap.CornerFacelets[i];
                int orientation = 0;
                for (int k = 0; k < 3; k++)
                {
                    byte col = c[f[k].face, f[k].row, f[k].col];
                    if (col == u || col == d) { orientation = k; break; }
                }
                coordinate = coordinate * 3 + orientation;
            }
            return coordinate;
        }

        /// <summary>Стандартна орієнтація ребер (відносно осі F/B): ребро з кольором U/D орієнтоване,
        /// якщо цей колір на наліпці 0 слоту; ребро середнього шару — якщо там колір F/B.</summary>
        private static int EdgeOrientation(Cube c)
        {
            byte u = c[Face.U, 1, 1], d = c[Face.D, 1, 1];
            byte f = c[Face.F, 1, 1], b = c[Face.B, 1, 1];
            int coordinate = 0;
            for (int i = 0; i < 12; i++)
            {
                var e = PieceMap.EdgeFacelets[i];
                byte c0 = c[e[0].face, e[0].row, e[0].col];
                byte c1 = c[e[1].face, e[1].row, e[1].col];

                int orientation;
                if (c0 == u || c0 == d) orientation = 0;
                else if (c1 == u || c1 == d) orientation = 1;
                else orientation = (c0 == f || c0 == b) ? 0 : 1;

                coordinate = coordinate * 2 + orientation;
            }
            return coordinate;
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
