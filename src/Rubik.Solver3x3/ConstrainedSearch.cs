using System;
using System.Collections.Generic;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    /// <summary>
    /// Generic breadth-first search directly on a real <see cref="Cube"/>: finds the shortest move
    /// sequence that reaches a goal, using a caller-supplied "state key" to collapse cube states that
    /// are equivalent for the current sub-problem (so search stays small even though the full cube's
    /// state space is astronomical). Because the goal is checked on the real engine, any sequence
    /// returned is correct by construction — there is nothing here to get subtly wrong by hand.
    /// </summary>
    public static class ConstrainedSearch
    {
        public static readonly string[] AllMoves =
        {
            "U","U'","U2","D","D'","D2","R","R'","R2","F","F'","F2","L","L'","L2","B","B'","B2"
        };

        /// <summary>Same as <see cref="AllMoves"/> but without D turns — last-layer algorithms never need them,
        /// and excluding them shrinks the search a lot.</summary>
        public static readonly string[] LastLayerMoves =
        {
            "U","U'","U2","R","R'","R2","F","F'","F2","L","L'","L2","B","B'","B2"
        };

        public static List<Move> Solve(
            Cube start, int maxDepth,
            Func<Cube, string> stateKey, Func<Cube, bool> isGoal,
            string[] moveSet)
        {
            if (isGoal(start)) return new List<Move>();

            // Group moves by their face letter, so we can forbid repeating the same face twice in a
            // row: two consecutive turns of one face are always redundant (equivalent to one of the
            // single tokens already in moveSet), and skipping them cuts branching substantially.
            var byFace = new Dictionary<char, List<string>>();
            foreach (var token in moveSet)
            {
                char face = token[0];
                if (!byFace.TryGetValue(face, out var list)) byFace[face] = list = new List<string>();
                list.Add(token);
            }

            var visited = new HashSet<string> { stateKey(start) };
            var queue = new Queue<(Cube cube, List<Move> moves, char lastFace)>();
            queue.Enqueue((start, new List<Move>(), '\0'));

            while (queue.Count > 0)
            {
                var (cube, moves, lastFace) = queue.Dequeue();
                if (moves.Count >= maxDepth) continue;

                foreach (var kv in byFace)
                {
                    if (kv.Key == lastFace) continue;
                    foreach (var token in kv.Value)
                    {
                        var next = cube.Clone();
                        var parsed = Notation.Parse(token, next.Size);
                        next.Apply(parsed);

                        var nextMoves = new List<Move>(moves.Count + 1);
                        nextMoves.AddRange(moves);
                        nextMoves.AddRange(parsed);

                        if (isGoal(next)) return nextMoves;

                        string key = stateKey(next);
                        if (visited.Add(key))
                            queue.Enqueue((next, nextMoves, kv.Key));
                    }
                }
            }

            throw new InvalidOperationException($"No solution found within depth {maxDepth}.");
        }
    }
}
