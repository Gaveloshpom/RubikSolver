using System;
using System.Collections.Generic;

namespace Rubik.Core
{
    public static class Scrambler
    {
        /// <summary>
        /// Random sequence of single-layer moves (all axes, 1..3 quarter turns). By default only outer
        /// layers are turned; <paramref name="innerLayers"/> also turns inner slices, which bigger cubes need
        /// to get properly mixed. Two moves in a row on the same layer are never generated.
        /// The same arguments give the same scramble.
        /// </summary>
        public static List<Move> Generate(int size, int length, int seed, bool innerLayers = false)
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
                    layer = innerLayers
                        ? rng.Next(size)
                        : rng.Next(2) == 0 ? 0 : size - 1; // outer faces only (0 or size - 1)
                } while (axis == lastAxis && layer == lastLayer);

                moves.Add(new Move((Axis)axis, layer, rng.Next(1, 4)));
                lastAxis = axis;
                lastLayer = layer;
            }
            return moves;
        }
    }
}
