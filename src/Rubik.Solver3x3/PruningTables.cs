using System;
using System.Collections.Generic;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    public static class PruningTables
    {
        public static readonly byte[] CornerOrientation = new byte[2187];
        public static readonly byte[] EdgeOrientation = new byte[2048];
        public static readonly byte[] UDSlice = new byte[4096]; // Надійна бітмаска замість Choose
        public static readonly byte[] CornerPermutation = new byte[40320];
        public static readonly byte[] EdgePermutation = new byte[40320];

        public static readonly byte[] P1_EdgeSlice = new byte[2048 * 4096];
        private static readonly int[,] EdgeO_Move = new int[2048, 18];
        private static readonly int[,] UDSlice_Move = new int[4096, 18];

        public static bool IsInitialized { get; private set; } = false;

        public static void Initialize(Cube solvedCube)
        {
            if (IsInitialized) return;

            var p1Moves = new Move[ConstrainedSearch.AllMoves.Length];
            for (int i = 0; i < p1Moves.Length; i++)
                p1Moves[i] = Notation.Parse(ConstrainedSearch.AllMoves[i], 3)[0];

            string[] p2Names = { "U", "U'", "U2", "D", "D'", "D2", "R2", "L2", "F2", "B2" };
            var p2Moves = new Move[p2Names.Length];
            for (int i = 0; i < p2Moves.Length; i++)
                p2Moves[i] = Notation.Parse(p2Names[i], 3)[0];

            // 1. Спочатку будуємо швидкі масиви переходів (Transition Tables)
            BuildTransitions(EdgeO_Move, GetEdgeOrientationCoordinate, 2048, solvedCube, p1Moves);
            BuildTransitions(UDSlice_Move, GetUDSliceCoordinate, 4096, solvedCube, p1Moves);

            // 2. Будуємо велику об'єднану таблицю для Фази 1 (генерується за мілісекунди!)
            int startEo = GetEdgeOrientationCoordinate(solvedCube);
            int startUd = GetUDSliceCoordinate(solvedCube);
            BuildCombinedPhase1Table(p1Moves.Length, startEo, startUd);

            // 3. Звичайні таблиці (EdgeOrientation та UDSlice більше не генеруємо окремо, вони є в P1_EdgeSlice)
            FillTable(CornerOrientation, GetCornerOrientationCoordinate, 2187, solvedCube, p1Moves);

            // 4. Фаза 2
            FillTable(CornerPermutation, GetCornerPermutationCoordinate, 40320, solvedCube, p2Moves);
            FillTable(EdgePermutation, GetEdgePermutationCoordinate, 40320, solvedCube, p2Moves);

            IsInitialized = true;
        }

        private static void FillTable(byte[] table, Func<Cube, int> getCoord, int maxStates, Cube solvedCube, Move[] moves)
        {
            for (int i = 0; i < maxStates; i++) table[i] = 255;

            var queue = new Queue<(Cube cube, byte depth)>();
            queue.Enqueue((solvedCube.Clone(), 0));

            int startCoord = getCoord(solvedCube);
            table[startCoord] = 0;
            int visited = 1;

            while (queue.Count > 0 && visited < maxStates)
            {
                var (current, depth) = queue.Dequeue();
                byte nextDepth = (byte)(depth + 1);

                foreach (var move in moves)
                {
                    var next = current.Clone();
                    next.Apply(new[] { move });

                    int coord = getCoord(next);
                    if (table[coord] == 255)
                    {
                        table[coord] = nextDepth;
                        visited++;
                        queue.Enqueue((next, nextDepth));
                    }
                }
            }
        }

        public static int GetCornerOrientationCoordinate(Cube c)
        {
            byte uCenter = c[Face.U, 1, 1], dCenter = c[Face.D, 1, 1];
            int coordinate = 0;
            for (int i = 0; i < 7; i++)
            {
                var facelets = PieceMap.CornerFacelets[i];
                byte colorU = c[facelets[0].face, facelets[0].row, facelets[0].col];
                byte colorR = c[facelets[1].face, facelets[1].row, facelets[1].col];
                byte colorF = c[facelets[2].face, facelets[2].row, facelets[2].col];

                int orientation = 0;
                if (colorU == uCenter || colorU == dCenter) orientation = 0;
                else if (colorR == uCenter || colorR == dCenter) orientation = 1;
                else if (colorF == uCenter || colorF == dCenter) orientation = 2;

                coordinate = coordinate * 3 + orientation;
            }
            return coordinate;
        }

        public static int GetEdgeOrientationCoordinate(Cube c)
        {
            byte uCenter = c[Face.U, 1, 1], dCenter = c[Face.D, 1, 1];
            byte fCenter = c[Face.F, 1, 1], bCenter = c[Face.B, 1, 1];
            int coordinate = 0;
            for (int i = 0; i < 11; i++)
            {
                var facelets = PieceMap.EdgeFacelets[i];
                byte c0 = c[facelets[0].face, facelets[0].row, facelets[0].col];
                byte c1 = c[facelets[1].face, facelets[1].row, facelets[1].col];

                int orientation = 0;
                if (c0 == uCenter || c0 == dCenter) orientation = 0;
                else if (c1 == uCenter || c1 == dCenter) orientation = 1;
                else if (c0 == fCenter || c0 == bCenter) orientation = 0;
                else orientation = 1;

                coordinate = coordinate * 2 + orientation;
            }
            return coordinate;
        }

        public static int GetUDSliceCoordinate(Cube c)
        {
            byte uCenter = c[Face.U, 1, 1], dCenter = c[Face.D, 1, 1];
            int mask = 0;
            for (int i = 0; i < 12; i++)
            {
                var facelets = PieceMap.EdgeFacelets[i];
                byte c0 = c[facelets[0].face, facelets[0].row, facelets[0].col];
                byte c1 = c[facelets[1].face, facelets[1].row, facelets[1].col];

                if (c0 != uCenter && c0 != dCenter && c1 != uCenter && c1 != dCenter)
                {
                    mask |= (1 << i); // Записуємо 1 на позицію i
                }
            }
            return mask; // Повертає значення від 0 до 4095
        }

        public static int GetCornerPermutationCoordinate(Cube c)
        {
            byte u = c[Face.U, 1, 1], d = c[Face.D, 1, 1];
            byte r = c[Face.R, 1, 1], l = c[Face.L, 1, 1], f = c[Face.F, 1, 1], b = c[Face.B, 1, 1];

            Span<int> perm = stackalloc int[8];

            for (int i = 0; i < 8; i++)
            {
                var facelets = PieceMap.CornerFacelets[i];
                byte c0 = c[facelets[0].face, facelets[0].row, facelets[0].col];
                byte c1 = c[facelets[1].face, facelets[1].row, facelets[1].col];
                byte c2 = c[facelets[2].face, facelets[2].row, facelets[2].col];

                // Використовуємо бітові маски кольорів для миттєвої ідентифікації кута без масивів
                int mask = (1 << c0) | (1 << c1) | (1 << c2);

                if (mask == ((1 << u) | (1 << r) | (1 << f))) perm[i] = 0;
                else if (mask == ((1 << u) | (1 << f) | (1 << l))) perm[i] = 1;
                else if (mask == ((1 << u) | (1 << l) | (1 << b))) perm[i] = 2;
                else if (mask == ((1 << u) | (1 << b) | (1 << r))) perm[i] = 3;
                else if (mask == ((1 << d) | (1 << f) | (1 << r))) perm[i] = 4;
                else if (mask == ((1 << d) | (1 << l) | (1 << f))) perm[i] = 5;
                else if (mask == ((1 << d) | (1 << b) | (1 << l))) perm[i] = 6;
                else if (mask == ((1 << d) | (1 << r) | (1 << b))) perm[i] = 7;
            }

            int index = 0, fact = 1;
            for (int i = 6; i >= 0; i--)
            {
                int count = 0;
                for (int j = i + 1; j < 8; j++)
                    if (perm[j] < perm[i]) count++;

                index += count * fact;
                fact *= (8 - i);
            }
            return index;
        }

        public static int GetEdgePermutationCoordinate(Cube c)
        {
            byte u = c[Face.U, 1, 1], d = c[Face.D, 1, 1];
            byte r = c[Face.R, 1, 1], l = c[Face.L, 1, 1], f = c[Face.F, 1, 1], b = c[Face.B, 1, 1];

            Span<int> perm = stackalloc int[8];
            int idx = 0;

            // Збираємо 8 ребер шарів U та D
            for (int i = 0; i < 12; i++)
            {
                var facelets = PieceMap.EdgeFacelets[i];
                byte c0 = c[facelets[0].face, facelets[0].row, facelets[0].col];
                byte c1 = c[facelets[1].face, facelets[1].row, facelets[1].col];

                // Якщо це ребро належить шару U або D
                if (c0 == u || c0 == d || c1 == u || c1 == d)
                {
                    int mask = (1 << c0) | (1 << c1);

                    if (mask == ((1 << u) | (1 << r))) perm[idx] = 0;
                    else if (mask == ((1 << u) | (1 << f))) perm[idx] = 1;
                    else if (mask == ((1 << u) | (1 << l))) perm[idx] = 2;
                    else if (mask == ((1 << u) | (1 << b))) perm[idx] = 3;
                    else if (mask == ((1 << d) | (1 << r))) perm[idx] = 4;
                    else if (mask == ((1 << d) | (1 << f))) perm[idx] = 5;
                    else if (mask == ((1 << d) | (1 << l))) perm[idx] = 6;
                    else if (mask == ((1 << d) | (1 << b))) perm[idx] = 7;

                    idx++;
                    if (idx == 8) break; // Знайшли всі 8 ребер
                }
            }

            int index = 0, fact = 1;
            for (int i = 6; i >= 0; i--)
            {
                int count = 0;
                for (int j = i + 1; j < 8; j++)
                    if (perm[j] < perm[i]) count++;

                index += count * fact;
                fact *= (8 - i);
            }
            return index;
        }

        private static void BuildTransitions(int[,] transTable, Func<Cube, int> getCoord, int maxStates, Cube solved, Move[] moves)
        {
            // Ініціалізуємо всі переходи як "невідомі" (-1)
            for (int i = 0; i < maxStates; i++)
                for (int m = 0; m < moves.Length; m++)
                    transTable[i, m] = -1;

            var coordQ = new Queue<(Cube c, int coord)>();
            bool[] visited = new bool[maxStates];

            int start = getCoord(solved);
            visited[start] = true;
            coordQ.Enqueue((solved.Clone(), start));

            while (coordQ.Count > 0)
            {
                var (currCube, currCoord) = coordQ.Dequeue();

                for (int i = 0; i < moves.Length; i++)
                {
                    if (transTable[currCoord, i] != -1) continue;

                    var nextCube = currCube.Clone();
                    nextCube.Apply(new[] { moves[i] });
                    int nextCoord = getCoord(nextCube);

                    transTable[currCoord, i] = nextCoord;

                    if (!visited[nextCoord])
                    {
                        visited[nextCoord] = true;
                        coordQ.Enqueue((nextCube, nextCoord));
                    }
                }
            }
        }

        private static void BuildCombinedPhase1Table(int movesCount, int startEo, int startUd)
        {
            // 2048 * 4096 = 8,388,608 байт (8 МБ). Заповнюємо значенням 255
            for (int i = 0; i < P1_EdgeSlice.Length; i++) P1_EdgeSlice[i] = 255;

            var q = new Queue<int>();

            int startCombined = startEo * 4096 + startUd;
            P1_EdgeSlice[startCombined] = 0;
            q.Enqueue(startCombined);

            while (q.Count > 0)
            {
                int curr = q.Dequeue();
                int eo = curr / 4096; // Дістаємо координату орієнтації ребер
                int ud = curr % 4096; // Дістаємо координату зрізу

                byte nextDepth = (byte)(P1_EdgeSlice[curr] + 1);

                for (int i = 0; i < movesCount; i++)
                {
                    int nextEo = EdgeO_Move[eo, i];
                    int nextUd = UDSlice_Move[ud, i];

                    if (nextEo == -1 || nextUd == -1) continue; // Цей перехід не існує

                    // Об'єднуємо дві координати в одну
                    int nextCombined = nextEo * 4096 + nextUd;

                    if (P1_EdgeSlice[nextCombined] == 255)
                    {
                        P1_EdgeSlice[nextCombined] = nextDepth;
                        q.Enqueue(nextCombined);
                    }
                }
            }
        }
    }
}