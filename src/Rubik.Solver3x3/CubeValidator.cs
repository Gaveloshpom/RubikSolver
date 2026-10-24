using System;

namespace Rubik.Solver3x3
{
    public enum CubeValidationError
    {
        None,
        WrongLength,
        NotFilled,      // some stickers are not filled in (value outside 0..5)
        WrongCount,     // some color does not have 9 stickers
        WrongCenters,   // the centres are not U R F D L B in place
        InvalidCorner,  // a corner with colors that never meet
        InvalidEdge,
        DuplicateCorner,
        DuplicateEdge,
        TwistedCorner,  // corner orientation sum is not divisible by 3
        FlippedEdge,    // edge orientation sum is odd
        Parity,         // corner and edge permutation parities differ (two pieces swapped)
    }

    public readonly struct CubeValidation
    {
        public CubeValidationError Error { get; }
        /// <summary>For NotFilled: the number of empty stickers; for WrongCount: the color.</summary>
        public int Detail { get; }
        /// <summary>For WrongCount: how many stickers of that color there are.</summary>
        public int Count { get; }

        public CubeValidation(CubeValidationError error, int detail = 0, int count = 0)
        {
            Error = error;
            Detail = detail;
            Count = count;
        }

        public bool IsValid => Error == CubeValidationError.None;
        public override string ToString() => $"{Error} ({Detail}, {Count})";
    }

    /// <summary>
    /// Checks whether a 3x3 cube given by stickers (as in Cube.Stickers, colors 0..5 = U R F D L B) is solvable.
    /// Indices match the Kociemba facelets (U1..U9, R1..R9, F, D, L, B), so the tables come from there.
    /// </summary>
    public static class CubeValidator
    {
        private const int U = 0, R = 1, F = 2, D = 3, L = 4, B = 5;

        internal static readonly int[][] CornerFacelets =
        {
            new[] { 8, 9, 20 }, new[] { 6, 18, 38 }, new[] { 0, 36, 47 }, new[] { 2, 45, 11 },
            new[] { 29, 26, 15 }, new[] { 27, 44, 24 }, new[] { 33, 53, 42 }, new[] { 35, 17, 51 },
        };

        internal static readonly int[][] CornerColors =
        {
            new[] { U, R, F }, new[] { U, F, L }, new[] { U, L, B }, new[] { U, B, R },
            new[] { D, F, R }, new[] { D, L, F }, new[] { D, B, L }, new[] { D, R, B },
        };

        internal static readonly int[][] EdgeFacelets =
        {
            new[] { 5, 10 }, new[] { 7, 19 }, new[] { 3, 37 }, new[] { 1, 46 },
            new[] { 32, 16 }, new[] { 28, 25 }, new[] { 30, 43 }, new[] { 34, 52 },
            new[] { 23, 12 }, new[] { 21, 41 }, new[] { 50, 39 }, new[] { 48, 14 },
        };

        internal static readonly int[][] EdgeColors =
        {
            new[] { U, R }, new[] { U, F }, new[] { U, L }, new[] { U, B },
            new[] { D, R }, new[] { D, F }, new[] { D, L }, new[] { D, B },
            new[] { F, R }, new[] { F, L }, new[] { B, L }, new[] { B, R },
        };

        public static CubeValidation Validate(ReadOnlySpan<byte> s)
        {
            if (s.Length != 54) return new CubeValidation(CubeValidationError.WrongLength);

            int empty = 0;
            Span<int> counts = stackalloc int[6];
            for (int i = 0; i < 54; i++)
            {
                if (s[i] > 5) empty++;
                else counts[s[i]]++;
            }
            if (empty > 0) return new CubeValidation(CubeValidationError.NotFilled, empty);
            for (int c = 0; c < 6; c++)
                if (counts[c] != 9) return new CubeValidation(CubeValidationError.WrongCount, c, counts[c]);
            for (int f = 0; f < 6; f++)
                if (s[f * 9 + 4] != f) return new CubeValidation(CubeValidationError.WrongCenters);

            // corners
            Span<int> cp = stackalloc int[8];
            int coSum = 0, usedCorners = 0;
            for (int i = 0; i < 8; i++)
            {
                var fl = CornerFacelets[i];
                int ori = 0;
                while (ori < 3 && s[fl[ori]] != U && s[fl[ori]] != D) ori++;
                if (ori == 3) return new CubeValidation(CubeValidationError.InvalidCorner);

                int c1 = s[fl[(ori + 1) % 3]], c2 = s[fl[(ori + 2) % 3]];
                int found = -1;
                for (int j = 0; j < 8; j++)
                    if (CornerColors[j][0] == s[fl[ori]] && CornerColors[j][1] == c1 && CornerColors[j][2] == c2) { found = j; break; }
                if (found < 0) return new CubeValidation(CubeValidationError.InvalidCorner);
                if ((usedCorners & (1 << found)) != 0) return new CubeValidation(CubeValidationError.DuplicateCorner);
                usedCorners |= 1 << found;
                cp[i] = found;
                coSum += ori;
            }

            // edges
            Span<int> ep = stackalloc int[12];
            int eoSum = 0, usedEdges = 0;
            for (int i = 0; i < 12; i++)
            {
                int a = s[EdgeFacelets[i][0]], b = s[EdgeFacelets[i][1]];
                int found = -1, flip = 0;
                for (int j = 0; j < 12; j++)
                {
                    if (EdgeColors[j][0] == a && EdgeColors[j][1] == b) { found = j; break; }
                    if (EdgeColors[j][0] == b && EdgeColors[j][1] == a) { found = j; flip = 1; break; }
                }
                if (found < 0) return new CubeValidation(CubeValidationError.InvalidEdge);
                if ((usedEdges & (1 << found)) != 0) return new CubeValidation(CubeValidationError.DuplicateEdge);
                usedEdges |= 1 << found;
                ep[i] = found;
                eoSum += flip;
            }

            if (coSum % 3 != 0) return new CubeValidation(CubeValidationError.TwistedCorner);
            if (eoSum % 2 != 0) return new CubeValidation(CubeValidationError.FlippedEdge);
            if (Parity(cp) != Parity(ep)) return new CubeValidation(CubeValidationError.Parity);

            return new CubeValidation(CubeValidationError.None);
        }

        private static int Parity(ReadOnlySpan<int> perm)
        {
            int inversions = 0;
            for (int i = 0; i < perm.Length; i++)
                for (int j = i + 1; j < perm.Length; j++)
                    if (perm[i] > perm[j]) inversions++;
            return inversions & 1;
        }
    }
}
