using System;
using System.Collections.Generic;

namespace Rubik.Core
{
    public static class Scrambler
    {
        /// <summary>
        /// Випадкова послідовність одношарових ходів (усі осі, усі шари, 1..3 чверті).
        /// Два ходи поспіль по одному й тому ж шарі не генеруються. Однаковий seed дає однаковий скрамбл.
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
                    layer = rng.Next(size);
                } while (axis == lastAxis && layer == lastLayer);

                moves.Add(new Move((Axis)axis, layer, rng.Next(1, 4)));
                lastAxis = axis;
                lastLayer = layer;
            }
            return moves;
        }
    }
}
