using System;
using System.Collections.Generic;
using System.Text;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    public static class MoveOptimizer
    {
        public static List<Move> Optimize(IEnumerable<Move> moves)
        {
            var optimized = new List<Move>();

            foreach (var m in moves)
            {
                if (m.IsIdentity) continue;

                if (optimized.Count > 0)
                {
                    var last = optimized[optimized.Count - 1];

                    // If the previous and the current move turn the same plane (axis + layers)
                    if (last.Axis == m.Axis && last.LayerFrom == m.LayerFrom && last.LayerTo == m.LayerTo)
                    {
                        optimized.RemoveAt(optimized.Count - 1);

                        // Add the turns (1=90°, 2=180°, 3=270°/-90°)
                        int newTurns = (last.Turns + m.Turns) % 4;

                        // If the turns do not cancel out, store the merged move
                        if (newTurns != 0)
                        {
                            optimized.Add(new Move(last.Axis, last.LayerFrom, last.LayerTo, newTurns));
                        }
                        continue;
                    }
                }
                optimized.Add(m);
            }

            return optimized;
        }
    }
}