using System;

namespace Rubik.Core
{
    /// <summary>
    /// A move: a range of adjacent layers [LayerFrom..LayerTo] along an axis, turned
    /// together by Turns clockwise quarter turns, looking from the POSITIVE side of the axis
    /// (from face R, U or F).
    ///
    /// One mechanism covers everything: an outer face (R), an inner slice (2R), a wide move (3Rw)
    /// and a whole-cube rotation (x, y, z).
    /// </summary>
    public readonly struct Move : IEquatable<Move>
    {
        public Axis Axis { get; }
        public int LayerFrom { get; }
        public int LayerTo { get; }

        /// <summary>Number of quarter turns, 0..3 (normalized modulo 4).</summary>
        public int Turns { get; }

        public Move(Axis axis, int layerFrom, int layerTo, int turns)
        {
            if ((int)axis < 0 || (int)axis > 2) throw new ArgumentOutOfRangeException(nameof(axis));
            if (layerFrom < 0) throw new ArgumentOutOfRangeException(nameof(layerFrom));
            if (layerTo < layerFrom) throw new ArgumentOutOfRangeException(nameof(layerTo));

            Axis = axis;
            LayerFrom = layerFrom;
            LayerTo = layerTo;
            Turns = ((turns % 4) + 4) % 4;
        }

        public Move(Axis axis, int layer, int turns) : this(axis, layer, layer, turns) { }

        public bool IsIdentity => Turns == 0;
        public int LayerCount => LayerTo - LayerFrom + 1;

        public Move Inverse() => new Move(Axis, LayerFrom, LayerTo, 4 - Turns);

        public bool Equals(Move other) =>
            Axis == other.Axis && LayerFrom == other.LayerFrom &&
            LayerTo == other.LayerTo && Turns == other.Turns;

        public override bool Equals(object obj) => obj is Move m && Equals(m);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Axis;
                h = h * 397 ^ LayerFrom;
                h = h * 397 ^ LayerTo;
                h = h * 397 ^ Turns;
                return h;
            }
        }

        public static bool operator ==(Move a, Move b) => a.Equals(b);
        public static bool operator !=(Move a, Move b) => !a.Equals(b);

        public override string ToString() => $"{Axis}[{LayerFrom}..{LayerTo}]x{Turns}";
    }
}
