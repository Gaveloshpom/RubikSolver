using System;
using System.Collections.Generic;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    public static class OptimalShortSearch
    {
        // Use every outer face move
        private static readonly string[] AllMoves = ConstrainedSearch.AllMoves;

        public static List<Move> Solve(Cube start, int maxDepth = 7)
        {
            if (IsSolved(start)) return new List<Move>();

            // Parse the moves once instead of inside the loop
            var parsedMoves = new Move[AllMoves.Length];
            for (int i = 0; i < AllMoves.Length; i++)
            {
                parsedMoves[i] = Notation.Parse(AllMoves[i], 3)[0];
            }

            var path = new List<Move>();
            var workCube = start.Clone();

            // Increase the depth step by step (iterative deepening)
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                if (Search(workCube, 0, depth, path, parsedMoves))
                {
                    return path;
                }
            }

            return null; // No solution within maxDepth
        }

        private static bool Search(Cube cube, int currentDepth, int maxDepth, List<Move> path, Move[] allMoves)
        {
            // Pruning with two tables:
            int coIndex = PruningTables.GetCornerOrientationCoordinate(cube);
            int eoIndex = PruningTables.GetEdgeOrientationCoordinate(cube);

            // Take the larger required move count of the two tables
            int minMovesRequired = Math.Max(
                PruningTables.CornerOrientation[coIndex],
                PruningTables.EdgeOrientation[eoIndex]
            );

            // If the remaining depth cannot even fix the orientation, cut the branch
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

                // Cut redundant branches:
                // Never turn the same face twice in a row (e.g. R followed by R2)
                if (path.Count > 0)
                {
                    var last = path[path.Count - 1];
                    if (last.Axis == move.Axis && last.LayerFrom == move.LayerFrom)
                        continue;
                }

                // Apply the move in place (no cube cloning, saves memory and time)
                cube.Apply(new[] { move });
                path.Add(move);

                // Go deeper
                if (Search(cube, currentDepth + 1, maxDepth, path, allMoves))
                {
                    return true;
                }

                // Backtrack: apply the inverse move
                path.RemoveAt(path.Count - 1);
                var inverse = new Move(move.Axis, move.LayerFrom, move.LayerTo, 4 - move.Turns);
                cube.Apply(new[] { inverse });
            }

            return false;
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