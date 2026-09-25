using Rubik.Core;

namespace Rubik.Solver3x3
{
    /// <summary>Which physical corner slot (3 faces meeting).</summary>
    public enum CornerSlot { URF, UFL, ULB, UBR, DFR, DLF, DBL, DRB }

    /// <summary>Which physical edge slot (2 faces meeting).</summary>
    public enum EdgeSlot { UF, UR, UB, UL, DF, DR, DB, DL, FR, FL, BR, BL }

    /// <summary>
    /// Maps every corner/edge slot to its sticker coordinates, for a 3x3 cube, using the same
    /// U/R/F/D/L/B face-and-axis convention as <see cref="Cube"/> (see its ToIndex/ToPoint).
    /// Facelet order within a slot always starts with the U or D facelet: for corners the
    /// remaining two follow clockwise looking from outside; for edges the second is the side face.
    /// Solved-cube colours (byte == Face index) at these coordinates ARE the piece's identity.
    /// </summary>
    public static class PieceMap
    {
        public static readonly (Face face, int row, int col)[][] CornerFacelets =
        {
            new[] { (Face.U, 2, 2), (Face.R, 0, 0), (Face.F, 0, 2) },   // URF
            new[] { (Face.U, 2, 0), (Face.F, 0, 0), (Face.L, 0, 2) },   // UFL
            new[] { (Face.U, 0, 0), (Face.L, 0, 0), (Face.B, 0, 2) },   // ULB
            new[] { (Face.U, 0, 2), (Face.B, 0, 0), (Face.R, 0, 2) },   // UBR
            new[] { (Face.D, 0, 2), (Face.F, 2, 2), (Face.R, 2, 0) },   // DFR
            new[] { (Face.D, 0, 0), (Face.L, 2, 2), (Face.F, 2, 0) },   // DLF
            new[] { (Face.D, 2, 0), (Face.B, 2, 2), (Face.L, 2, 0) },   // DBL
            new[] { (Face.D, 2, 2), (Face.R, 2, 2), (Face.B, 2, 0) },   // DRB
        };

        public static readonly (Face face, int row, int col)[][] EdgeFacelets =
        {
            new[] { (Face.U, 2, 1), (Face.F, 0, 1) },   // UF
            new[] { (Face.U, 1, 2), (Face.R, 0, 1) },   // UR
            new[] { (Face.U, 0, 1), (Face.B, 0, 1) },   // UB
            new[] { (Face.U, 1, 0), (Face.L, 0, 1) },   // UL
            new[] { (Face.D, 0, 1), (Face.F, 2, 1) },   // DF
            new[] { (Face.D, 1, 2), (Face.R, 2, 1) },   // DR
            new[] { (Face.D, 2, 1), (Face.B, 2, 1) },   // DB
            new[] { (Face.D, 1, 0), (Face.L, 2, 1) },   // DL
            new[] { (Face.F, 1, 2), (Face.R, 1, 0) },   // FR
            new[] { (Face.F, 1, 0), (Face.L, 1, 2) },   // FL
            new[] { (Face.B, 1, 0), (Face.R, 1, 2) },   // BR
            new[] { (Face.B, 1, 2), (Face.L, 1, 0) },   // BL
        };

        public static byte[] ColorsAt(Cube c, (Face face, int row, int col)[] facelets)
        {
            var result = new byte[facelets.Length];
            for (int i = 0; i < facelets.Length; i++)
                result[i] = c[facelets[i].face, facelets[i].row, facelets[i].col];
            return result;
        }
    }
}
