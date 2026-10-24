using System;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    /// <summary>
    /// Cubie model, coordinates, move tables and pruning tables of the two-phase (Kociemba) solver.
    ///
    /// Cubie state (Kociemba conventions, same facelet tables as <see cref="CubeValidator"/>):
    /// cp[i] / ep[i] = which corner / edge sits at position i, co[i] / eo[i] = its twist / flip.
    /// Applying move m to state s is the product s * m:
    ///   (s*m).p[i] = s.p[m.p[i]],  (s*m).o[i] = s.o[m.p[i]] + m.o[i].
    ///
    /// Moves are numbered 3 * face + (quarter turns - 1) with faces U R F D L B, so U U2 U' R R2 R' ...
    ///
    /// Phase 1 (all 18 moves) brings the cube into G1 = &lt;U, D, R2, F2, L2, B2&gt;:
    ///   twist (CO, 3^7 = 2187), flip (EO, 2^11 = 2048), slice (positions of the 4 UD-slice edges, C(12,4) = 495).
    /// Phase 2 (10 moves) solves G1:
    ///   corner permutation (8! = 40320), U/D edge permutation (8! = 40320), slice edge permutation (4! = 24).
    ///
    /// Everything is built once in the static constructor, which the CLR runs exactly once and thread-safely;
    /// afterwards the tables are read-only, so any number of threads can search at the same time.
    /// </summary>
    internal static class KociembaTables
    {
        public const int MoveCount = 18;
        public const int Phase2MoveCount = 10;

        public const int TwistCount = 2187;
        public const int FlipCount = 2048;
        public const int SliceCount = 495;
        public const int SliceSortedCount = 11880; // 495 * 24: slice positions and the order of the 4 slice edges
        public const int CornerPermCount = 40320;
        public const int UDEdgePermCount = 40320;
        public const int SlicePermCount = 24;

        private const int FR = 8; // first UD-slice edge (FR FL BL BR = 8..11)

        /// <summary>Phase 2 moves as indices into the 18 moves: U U2 U' D D2 D' R2 F2 L2 B2.</summary>
        public static readonly int[] Phase2Moves = { 0, 1, 2, 9, 10, 11, 4, 7, 13, 16 };

        /// <summary>The 18 moves as <see cref="Move"/> values of the sticker cube.</summary>
        public static readonly Move[] Moves = new Move[MoveCount];

        // Cubie form of the 18 moves: [move][position].
        public static readonly byte[][] MoveCp = new byte[MoveCount][];
        public static readonly byte[][] MoveCo = new byte[MoveCount][];
        public static readonly byte[][] MoveEp = new byte[MoveCount][];
        public static readonly byte[][] MoveEo = new byte[MoveCount][];

        // Move tables: [coordinate * moves + move]. Phase 1 tables use all 18 moves, phase 2 tables the 10 phase 2 moves.
        public static readonly ushort[] TwistMove;
        public static readonly ushort[] FlipMove;
        public static readonly ushort[] SliceSortedMove;
        public static readonly ushort[] CornerPermMove;
        public static readonly ushort[] UDEdgePermMove;
        public static readonly ushort[] SlicePermMove;

        // Pruning tables: exact distance to the goal of a pair of coordinates (lower bounds for the whole cube).
        public static readonly sbyte[] SliceTwistPrun;   // [slice * 2187 + twist]
        public static readonly sbyte[] SliceFlipPrun;    // [slice * 2048 + flip]
        public static readonly sbyte[] CornerSlicePrun;  // [cornerPerm * 24 + slicePerm]
        public static readonly sbyte[] EdgeSlicePrun;    // [udEdgePerm * 24 + slicePerm]

        static KociembaTables()
        {
            string[] faces = { "U", "R", "F", "D", "L", "B" };
            string[] suffix = { "", "2", "'" };
            Span<int> cp = stackalloc int[8], co = stackalloc int[8], ep = stackalloc int[12], eo = stackalloc int[12];
            for (int m = 0; m < MoveCount; m++)
            {
                Moves[m] = Notation.Parse(faces[m / 3] + suffix[m % 3], 3)[0];

                // Read the cubie form of each move off the sticker cube, so it always matches Cube.Apply.
                var cube = new Cube(3);
                cube.Apply(Moves[m]);
                if (!TryGetCubies(cube.Stickers, cp, co, ep, eo))
                    throw new InvalidOperationException("Sticker layout does not match the Kociemba facelet tables.");
                MoveCp[m] = ToBytes(cp);
                MoveCo[m] = ToBytes(co);
                MoveEp[m] = ToBytes(ep);
                MoveEo[m] = ToBytes(eo);
            }

            var all = new int[MoveCount];
            for (int m = 0; m < MoveCount; m++) all[m] = m;

            TwistMove = BuildMoveTable(TwistCount, all, corners: true, (p, o) => Twist(o));
            FlipMove = BuildMoveTable(FlipCount, all, corners: false, (p, o) => Flip(o));
            SliceSortedMove = BuildMoveTable(SliceSortedCount, all, corners: false, (p, o) => SliceSorted(p));
            CornerPermMove = BuildMoveTable(CornerPermCount, Phase2Moves, corners: true, (p, o) => PermRank(p, 0, 8, 0));
            UDEdgePermMove = BuildMoveTable(UDEdgePermCount, Phase2Moves, corners: false, (p, o) => PermRank(p, 0, 8, 0));

            // In G1 the slice edges stay in the slice, so the slice-sorted coordinate stays below 24 and is the slice permutation.
            SlicePermMove = new ushort[SlicePermCount * Phase2MoveCount];
            for (int s = 0; s < SlicePermCount; s++)
                for (int j = 0; j < Phase2MoveCount; j++)
                    SlicePermMove[s * Phase2MoveCount + j] = SliceSortedMove[s * MoveCount + Phase2Moves[j]];

            // The slice position alone (slice sorted / 24) moves independently of the edge order.
            var sliceMove = new ushort[SliceCount * MoveCount];
            for (int s = 0; s < SliceCount; s++)
                for (int m = 0; m < MoveCount; m++)
                    sliceMove[s * MoveCount + m] = (ushort)(SliceSortedMove[s * SlicePermCount * MoveCount + m] / SlicePermCount);

            SliceTwistPrun = BuildPruningTable(SliceCount, sliceMove, TwistCount, TwistMove, MoveCount);
            SliceFlipPrun = BuildPruningTable(SliceCount, sliceMove, FlipCount, FlipMove, MoveCount);
            CornerSlicePrun = BuildPruningTable(CornerPermCount, CornerPermMove, SlicePermCount, SlicePermMove, Phase2MoveCount);
            EdgeSlicePrun = BuildPruningTable(UDEdgePermCount, UDEdgePermMove, SlicePermCount, SlicePermMove, Phase2MoveCount);
        }

        /// <summary>Forces the static constructor (table build) to run now.</summary>
        public static void EnsureBuilt() { }

        // ---------------------------------------------------------------- facelets -> cubies

        /// <summary>
        /// Converts 54 stickers (colors 0..5 = U R F D L B, centres in place) into cubies.
        /// Returns false if some corner or edge has colors that cannot occur, or a piece occurs twice.
        /// Twist / flip / parity are not checked here (the pruning tables reject unreachable states).
        /// </summary>
        public static bool TryGetCubies(ReadOnlySpan<byte> s, Span<int> cp, Span<int> co, Span<int> ep, Span<int> eo)
        {
            int used = 0;
            for (int i = 0; i < 8; i++)
            {
                var fl = CubeValidator.CornerFacelets[i];
                int ori = 0;
                while (ori < 3 && s[fl[ori]] != 0 && s[fl[ori]] != 3) ori++;
                if (ori == 3) return false;
                int c0 = s[fl[ori]], c1 = s[fl[(ori + 1) % 3]], c2 = s[fl[(ori + 2) % 3]];
                int found = -1;
                for (int j = 0; j < 8; j++)
                {
                    var cc = CubeValidator.CornerColors[j];
                    if (cc[0] == c0 && cc[1] == c1 && cc[2] == c2) { found = j; break; }
                }
                if (found < 0 || (used & (1 << found)) != 0) return false;
                used |= 1 << found;
                cp[i] = found;
                co[i] = ori;
            }

            used = 0;
            for (int i = 0; i < 12; i++)
            {
                int a = s[CubeValidator.EdgeFacelets[i][0]], b = s[CubeValidator.EdgeFacelets[i][1]];
                int found = -1, flip = 0;
                for (int j = 0; j < 12; j++)
                {
                    var ec = CubeValidator.EdgeColors[j];
                    if (ec[0] == a && ec[1] == b) { found = j; break; }
                    if (ec[0] == b && ec[1] == a) { found = j; flip = 1; break; }
                }
                if (found < 0 || (used & (1 << found)) != 0) return false;
                used |= 1 << found;
                ep[i] = found;
                eo[i] = flip;
            }
            return true;
        }

        // ---------------------------------------------------------------- coordinates

        public static int Twist(ReadOnlySpan<byte> co)
        {
            int c = 0;
            for (int i = 0; i < 7; i++) c = c * 3 + co[i];
            return c;
        }

        public static int Flip(ReadOnlySpan<byte> eo)
        {
            int c = 0;
            for (int i = 0; i < 11; i++) c = c * 2 + eo[i];
            return c;
        }

        /// <summary>24 * (which 4 positions hold the slice edges) + (their order); 0 when solved.</summary>
        public static int SliceSorted(ReadOnlySpan<byte> ep)
        {
            int a = 0, x = 0;
            Span<byte> order = stackalloc byte[4];
            for (int j = 11; j >= 0; j--)
            {
                if (ep[j] >= FR)
                {
                    a += Binomial[11 - j, x + 1];
                    order[3 - x] = ep[j];
                    x++;
                }
            }
            return a * SlicePermCount + PermRank(order, 0, 4, FR);
        }

        /// <summary>Lehmer rank of p[from..from+n) whose values are base..base+n-1; the identity has rank 0.</summary>
        public static int PermRank(ReadOnlySpan<byte> p, int from, int n, int valueBase)
        {
            // For each element, count the smaller values still unused (= smaller values to its right).
            int rank = 0, used = 0;
            for (int i = 0; i < n; i++)
            {
                int v = p[from + i] - valueBase;
                rank = rank * (n - i) + v - BitCount[used & ((1 << v) - 1)];
                used |= 1 << v;
            }
            return rank;
        }

        private static readonly byte[] BitCount = BuildBitCount();

        private static byte[] BuildBitCount()
        {
            var t = new byte[1 << 12];
            for (int i = 1; i < t.Length; i++) t[i] = (byte)(t[i >> 1] + (i & 1));
            return t;
        }

        // Binomial[n, k] for n < 12, k <= 4 (0 when k > n).
        private static readonly int[,] Binomial = BuildBinomial();

        private static int[,] BuildBinomial()
        {
            var t = new int[12, 5];
            for (int n = 0; n < 12; n++)
            {
                t[n, 0] = 1;
                for (int k = 1; k <= 4; k++) t[n, k] = n == 0 ? 0 : t[n - 1, k - 1] + t[n - 1, k];
            }
            return t;
        }

        // ---------------------------------------------------------------- table builders

        private delegate int Encoder(ReadOnlySpan<byte> perm, ReadOnlySpan<byte> ori);

        /// <summary>
        /// BFS from the solved state over the coordinate graph. One representative cubie state is kept
        /// per coordinate value: each coordinate fully describes the pieces it depends on, so the result
        /// of a move does not depend on which representative is used. No decoders needed.
        /// </summary>
        private static ushort[] BuildMoveTable(int count, int[] moves, bool corners, Encoder encode)
        {
            int n = corners ? 8 : 12, mod = corners ? 3 : 2, nm = moves.Length;
            var table = new ushort[count * nm];
            var reps = new byte[count * 2 * n]; // perm then ori, per coordinate value
            var known = new bool[count];
            var queue = new int[count];

            int start = encode(Identity(n), new byte[n]);
            Array.Copy(Identity(n), 0, reps, start * 2 * n, n);
            known[start] = true;
            queue[0] = start;
            int head = 0, tail = 1;

            var next = new byte[2 * n];
            while (head < tail)
            {
                int c = queue[head++];
                int off = c * 2 * n;
                for (int k = 0; k < nm; k++)
                {
                    int m = moves[k];
                    var mp = corners ? MoveCp[m] : MoveEp[m];
                    var mo = corners ? MoveCo[m] : MoveEo[m];
                    for (int i = 0; i < n; i++)
                    {
                        next[i] = reps[off + mp[i]];
                        next[n + i] = (byte)((reps[off + n + mp[i]] + mo[i]) % mod);
                    }
                    int nc = encode(new ReadOnlySpan<byte>(next, 0, n), new ReadOnlySpan<byte>(next, n, n));
                    table[c * nm + k] = (ushort)nc;
                    if (!known[nc])
                    {
                        known[nc] = true;
                        Array.Copy(next, 0, reps, nc * 2 * n, 2 * n);
                        queue[tail++] = nc;
                    }
                }
            }
            if (tail != count) throw new InvalidOperationException("Move table BFS did not reach every coordinate.");
            return table;
        }

        /// <summary>BFS distance table over the pair (a, b), index a * countB + b, both 0 at the goal.</summary>
        private static sbyte[] BuildPruningTable(int countA, ushort[] moveA, int countB, ushort[] moveB, int nm)
        {
            int size = countA * countB;
            var dist = new sbyte[size];
            for (int i = 0; i < size; i++) dist[i] = -1;
            var queue = new int[size];
            dist[0] = 0;
            int head = 0, tail = 1;
            while (head < tail)
            {
                int idx = queue[head++];
                int a = idx / countB, b = idx - a * countB;
                sbyte d = (sbyte)(dist[idx] + 1);
                for (int k = 0; k < nm; k++)
                {
                    int next = moveA[a * nm + k] * countB + moveB[b * nm + k];
                    if (dist[next] < 0)
                    {
                        dist[next] = d;
                        queue[tail++] = next;
                    }
                }
            }
            return dist;
        }

        private static byte[] Identity(int n)
        {
            var p = new byte[n];
            for (int i = 0; i < n; i++) p[i] = (byte)i;
            return p;
        }

        private static byte[] ToBytes(ReadOnlySpan<int> v)
        {
            var b = new byte[v.Length];
            for (int i = 0; i < v.Length; i++) b[i] = (byte)v[i];
            return b;
        }
    }
}
