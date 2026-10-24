using System;
using System.Collections.Generic;

namespace Rubik.Core
{
    public static class Scrambler
    {
        /// <summary>
        /// Random sequence of single-layer moves (all axes, outer layers, 1..3 quarter turns).
        /// Two moves in a row on the same layer are never generated. The same seed gives the same scramble.
        /// </summary>
        public static List<Move> Generate(int size, int length, int seed)
        {
            if (size < 1) throw new ArgumentOutOfRangeException(nameof(size));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

            var rng = new Random(seed);
            var moves = new List<Move>(length);
            int lastAxis = -1, lastLayer = -1;

            for (int i = 0; i < length; i++)
            {
                int axis, layer;
                do
                {
                    axis = rng.Next(3);
                    // Only outer faces are turned (0 or size - 1)
                    layer = rng.Next(2) == 0 ? 0 : size - 1;
                } while (axis == lastAxis && layer == lastLayer);

                moves.Add(new Move((Axis)axis, layer, rng.Next(1, 4)));
                lastAxis = axis;
                lastLayer = layer;
            }
            return moves;
        }
    }
}
