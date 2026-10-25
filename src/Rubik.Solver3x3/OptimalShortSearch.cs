using System;
using System.Collections.Generic;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    /// <summary>
    /// IDA* search for the shortest solution of short scrambles (up to maxDepth moves).
    ///
    /// The search runs on corner (CO) and edge (EO) orientation coordinates with its own transition tables,
    /// and the real cube is built and checked only at leaves where CO and EO are already zero. The tables
    /// are built here (lazily, once) and do not depend on <see cref="PruningTables.Initialize"/>:
    /// pruning used to read PruningTables, which were zero without Initialize (and EdgeOrientation
    /// always was), so a depth-7 search visited ~10^8 nodes on the sticker cube
    /// and "hung" on every scramble that cannot be solved in 7 moves.
    /// </summary>
    public static class OptimalShortSearch
    {
        // Use every outer face move
        private static readonly string[] AllMoves = ConstrainedSearch.AllMoves;

        private const int CoCount = 6561;   // 3^8: orientation of all 8 corners
        private const int EoCount = 4096;   // 2^12: orientation of all 12 edges
        private const byte Unreachable = 255;

        private static readonly Move[] Moves;
        private static readonly int[,] CoMove = new int[CoCount, 18];
        private static readonly int[,] EoMove = new int[EoCount, 18];
        private static readonly byte[] CoDist = new byte[CoCount];
        private static readonly byte[] EoDist = new byte[EoCount];

        // The CLR runs a static constructor exactly once and thread-safely (Unity searches in the background).
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
            // Unreachable orientation (twisted corner / flipped edge): the cube cannot be solved at all.
            if (CoDist[co] == Unreachable || EoDist[eo] == Unreachable) return null;

            var path = new int[maxDepth];

            // Increase the depth step by step (iterative deepening)
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                if (Search(start, co, eo, 0, depth, path))
                {
                    var result = new List<Move>(depth);
                    for (int i = 0; i < depth; i++) result.Add(Moves[path[i]]);
                    return result;
                }
            }

            return null; // No solution within maxDepth
        }

        private static bool Search(Cube start, int co, int eo, int currentDepth, int maxDepth, int[] path)
        {
            // Pruning: at least this many moves are needed just to orient corners / edges
            int remaining = maxDepth - currentDepth;
            if (CoDist[co] > remaining || EoDist[eo] > remaining) return false;

            if (remaining == 0)
            {
                // Orientation is already correct (otherwise it would have been pruned above), so check the real cube.
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
                        // Never turn the same face twice in a row (R then R2 is a single move),
                        // and opposite faces commute (L R == R L), so keep only one order.
                        if (move.LayerFrom <= last.LayerFrom) continue;
                    }
                }

                path[currentDepth] = i;
                if (Search(start, CoMove[co, i], EoMove[eo, i], currentDepth + 1, maxDepth, path))
                    return true;
            }

            return false;
        }

        /// <summary>BFS from the solved cube over the coordinate graph: fills the transition table and
        /// the distance to zero. One representative cube is kept per coordinate value:
        /// orientation after a move does not depend on permutation, so one representative is enough.</summary>
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

        /// <summary>For each of the 8 corners: which of its stickers (0, 1, 2) has the U or D centre color.</summary>
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

        /// <summary>Standard edge orientation (relative to the F/B axis): an edge with a U/D color is oriented
        /// if that color is on sticker 0 of the slot; a middle-layer edge is oriented if F/B is there.</summary>
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
            // Check that every face has a single color
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
            // Take the first 7 corners (PieceMap has 8 slots).
            // For each slot find its current orientation (0, 1 or 2)
            for (int i = 0; i < 7; i++)
            {
                CornerSlot slot = (CornerSlot)i;
                var faces = PieceMap.CornerFacelets[i]; //[cite: 4]

                // Find the corner by its home colors and look at its PrimaryIndex
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
