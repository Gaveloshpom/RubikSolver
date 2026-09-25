using System;
using System.Collections.Generic;
using System.Linq;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    /// <summary>Where a piece currently is, and how it's twisted/flipped relative to solved.</summary>
    public readonly struct Located<TSlot>
    {
        public TSlot Slot { get; }
        /// <summary>Index (0-based) of the facelet that currently holds the piece's "primary" colour
        /// (the one that belongs on U or D in the solved cube). 0 means correctly oriented.</summary>
        public int PrimaryIndex { get; }
        public Located(TSlot slot, int primaryIndex) { Slot = slot; PrimaryIndex = primaryIndex; }
    }

    public static class PieceFinder
    {
        private static readonly CornerSlot[] CornerSlots = (CornerSlot[])Enum.GetValues(typeof(CornerSlot));
        private static readonly EdgeSlot[] EdgeSlots = (EdgeSlot[])Enum.GetValues(typeof(EdgeSlot));

        public static Located<CornerSlot> FindCorner(Cube c, byte c0, byte c1, byte c2)
        {
            for (int s = 0; s < CornerSlots.Length; s++)
            {
                var f = PieceMap.CornerFacelets[s];
                byte a = c[f[0].face, f[0].row, f[0].col];
                byte b = c[f[1].face, f[1].row, f[1].col];
                byte d = c[f[2].face, f[2].row, f[2].col];

                // Is {a,b,d} == {c0,c1,c2} as a set? (only 3 elements, cheap to check directly)
                if (!((a == c0 || a == c1 || a == c2) && (b == c0 || b == c1 || b == c2) && (d == c0 || d == c1 || d == c2) && a != b && b != d && a != d))
                    continue;

                int primary = a == c0 ? 0 : b == c0 ? 1 : 2;
                return new Located<CornerSlot>(CornerSlots[s], primary);
            }
            throw new InvalidOperationException($"Corner {{{c0},{c1},{c2}}} not found (cube is corrupted).");
        }

        public static Located<EdgeSlot> FindEdge(Cube c, byte c0, byte c1)
        {
            for (int s = 0; s < EdgeSlots.Length; s++)
            {
                var f = PieceMap.EdgeFacelets[s];
                byte a = c[f[0].face, f[0].row, f[0].col];
                byte b = c[f[1].face, f[1].row, f[1].col];

                if (a == c0 && b == c1) return new Located<EdgeSlot>(EdgeSlots[s], 0);
                if (a == c1 && b == c0) return new Located<EdgeSlot>(EdgeSlots[s], 1);
            }
            throw new InvalidOperationException($"Edge {{{c0},{c1}}} not found (cube is corrupted).");
        }

        /// <summary>Colour currently sitting at a corner facelet, by slot and facelet index (0,1,2).</summary>
        public static byte CornerColor(Cube c, CornerSlot slot, int i)
        {
            var f = PieceMap.CornerFacelets[(int)slot][i];
            return c[f.face, f.row, f.col];
        }

        /// <summary>Colour currently sitting at an edge facelet, by slot and facelet index (0,1).</summary>
        public static byte EdgeColor(Cube c, EdgeSlot slot, int i)
        {
            var f = PieceMap.EdgeFacelets[(int)slot][i];
            return c[f.face, f.row, f.col];
        }
    }
}
