using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Rubik.Core;
using static Rubik.Solver3x3.KociembaTables;

namespace Rubik.Solver3x3
{
    /// <summary>
    /// Two-phase (Kociemba) solver for the 3x3 cube, working on coordinates only (see <see cref="KociembaTables"/>).
    ///
    /// Phase 1 is an IDA* over (twist, flip, slice) with all 18 moves into G1 = &lt;U, D, R2, F2, L2, B2&gt;;
    /// every phase 1 solution is followed by an IDA* over (corner perm, U/D edge perm, slice perm) with the
    /// 10 G1 moves. After the first solution, phase 1 keeps searching (longer phase 1, shorter phase 2) for a
    /// shorter total until the solution is short enough or the improvement budget is spent.
    ///
    /// Thread safe: the tables are built once and read-only, all search state lives in a per-call object.
    /// </summary>
    public static class KociembaSolver
    {
        /// <summary>Stop improving once a solution this short is found.</summary>
        private const int TargetLength = 20;

        /// <summary>
        /// ... but only once phase 1 has reached this depth. All shorter phase 1 depths are then searched
        /// exhaustively, so a cube that can be solved in this many moves or fewer gets an optimal solution
        /// (a few-move scramble is not answered with a 15-move solution). Cheap: random cubes need about
        /// this phase 1 depth anyway.
        /// </summary>
        private const int EarlyStopPhase1Depth = 8;

        /// <summary>How long to keep looking for a shorter solution after the first one.</summary>
        private static readonly TimeSpan ImproveBudget = TimeSpan.FromMilliseconds(100);

        private const int MaxPhase1Depth = 20;
        private const int MaxPhase2Depth = 18;
        private const int NoSolution = 99;

        /// <summary>Builds the tables now (takes a fraction of a second) instead of on the first solve.</summary>
        public static void Initialize() => EnsureBuilt();

        public static List<Move> Solve(Cube start) => Solve(start, Timeout.InfiniteTimeSpan);

        /// <summary>
        /// Same as <see cref="Solve(Cube)"/>, but gives up once <paramref name="timeLimit"/> has passed:
        /// returns the best solution found so far, or null if there is none yet.
        /// Also returns null for a cube that is not a solvable 3x3 cube.
        /// </summary>
        public static List<Move> Solve(Cube start, TimeSpan timeLimit)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (start.Size != 3) return null;
            if (start.IsSolved) return new List<Move>();

            long now = Stopwatch.GetTimestamp();
            long deadline = timeLimit == Timeout.InfiniteTimeSpan || timeLimit.TotalSeconds * Stopwatch.Frequency >= long.MaxValue - now
                ? long.MaxValue
                : now + (long)(Math.Max(0, timeLimit.TotalSeconds) * Stopwatch.Frequency);

            var search = new Search(deadline);
            if (!search.SetStart(start.Stickers)) return null;
            return search.Run();
        }

        private sealed class Search
        {
            private readonly long _deadline;
            private readonly long _improveTicks = (long)(ImproveBudget.TotalSeconds * Stopwatch.Frequency);
            private long _softDeadline = long.MaxValue;
            private int _nodes;
            private bool _stop;

            private readonly byte[] _cp = new byte[8], _co = new byte[8], _ep = new byte[12], _eo = new byte[12];

            private readonly int[] _path1 = new int[MaxPhase1Depth];
            private readonly int[] _path2 = new int[MaxPhase2Depth];
            private int _length1;

            private readonly int[] _best = new int[MaxPhase1Depth + MaxPhase2Depth];
            private int _bestLength = NoSolution;

            // Scratch for replaying phase 1 on permutations.
            private readonly byte[] _tmpC = new byte[8], _tmpC2 = new byte[8], _tmpE = new byte[12], _tmpE2 = new byte[12];

            public Search(long deadline) => _deadline = deadline;

            /// <summary>Reads the stickers into cubies. Colors are taken relative to the centres,
            /// so a cube held in any orientation works.</summary>
            public bool SetStart(ReadOnlySpan<byte> stickers)
            {
                Span<int> faceOfColor = stackalloc int[6];
                for (int c = 0; c < 6; c++) faceOfColor[c] = -1;
                for (int f = 0; f < 6; f++)
                {
                    byte c = stickers[f * 9 + 4];
                    if (c > 5 || faceOfColor[c] >= 0) return false;
                    faceOfColor[c] = f;
                }

                Span<byte> s = stackalloc byte[54];
                for (int i = 0; i < 54; i++)
                {
                    if (stickers[i] > 5) return false;
                    s[i] = (byte)faceOfColor[stickers[i]];
                }

                // Twist, flip and parity must be checked here: the coordinates leave out the last corner / edge.
                if (!CubeValidator.Validate(s).IsValid) return false;

                Span<int> cp = stackalloc int[8], co = stackalloc int[8], ep = stackalloc int[12], eo = stackalloc int[12];
                if (!TryGetCubies(s, cp, co, ep, eo)) return false;
                for (int i = 0; i < 8; i++) { _cp[i] = (byte)cp[i]; _co[i] = (byte)co[i]; }
                for (int i = 0; i < 12; i++) { _ep[i] = (byte)ep[i]; _eo[i] = (byte)eo[i]; }
                return true;
            }

            public List<Move> Run()
            {
                int twist = KociembaTables.Twist(_co), flip = KociembaTables.Flip(_eo), slice = KociembaTables.SliceSorted(_ep);
                int h = Phase1Bound(twist, flip, slice);

                for (_length1 = h; _length1 <= MaxPhase1Depth && _length1 < _bestLength && !_stop && !GoodEnough(); _length1++)
                    Phase1(twist, flip, slice, 0, _length1);

                if (_bestLength == NoSolution) return null;
                var result = new List<Move>(_bestLength);
                for (int i = 0; i < _bestLength; i++) result.Add(Moves[_best[i]]);
                return result;
            }

            private static int Phase1Bound(int twist, int flip, int sliceSorted)
            {
                int slice = sliceSorted / SlicePermCount;
                return Math.Max(SliceTwistPrun[slice * TwistCount + twist], SliceFlipPrun[slice * FlipCount + flip]);
            }

            /// <summary>Returns true when the whole search must stop.</summary>
            private bool Phase1(int twist, int flip, int slice, int depth, int togo)
            {
                if (togo == 0)
                {
                    // In G1 now. If the last move is a G1 move, the cube was in G1 one move earlier and this
                    // phase 1 is not new (it was handled by the shorter phase 1).
                    if (depth > 0 && IsPhase2Move(_path1[depth - 1])) return false;
                    return StartPhase2(slice);
                }

                int last = depth > 0 ? _path1[depth - 1] : -1;
                for (int m = 0; m < MoveCount; m++)
                {
                    if (Redundant(last, m)) continue;

                    int nt = TwistMove[twist * MoveCount + m];
                    int nf = FlipMove[flip * MoveCount + m];
                    int ns = SliceSortedMove[slice * MoveCount + m];
                    if (Phase1Bound(nt, nf, ns) > togo - 1) continue;

                    _path1[depth] = m;
                    if (Phase1(nt, nf, ns, depth + 1, togo - 1)) return true;
                    if (TimeUp()) return true;
                }
                return false;
            }

            private bool StartPhase2(int slicePerm)
            {
                // Replay phase 1 on the permutations (orientations are solved in G1).
                Array.Copy(_cp, _tmpC, 8);
                Array.Copy(_ep, _tmpE, 12);
                for (int d = 0; d < _length1; d++)
                {
                    int m = _path1[d];
                    var mc = MoveCp[m];
                    for (int i = 0; i < 8; i++) _tmpC2[i] = _tmpC[mc[i]];
                    Array.Copy(_tmpC2, _tmpC, 8);
                    var me = MoveEp[m];
                    for (int i = 0; i < 12; i++) _tmpE2[i] = _tmpE[me[i]];
                    Array.Copy(_tmpE2, _tmpE, 12);
                }
                int corners = PermRank(_tmpC, 0, 8, 0);
                int edges = PermRank(_tmpE, 0, 8, 0);

                int h = Phase2Bound(corners, edges, slicePerm);
                int maxDepth = Math.Min(MaxPhase2Depth, _bestLength - 1 - _length1);
                for (int d = h; d <= maxDepth; d++)
                {
                    if (Phase2(corners, edges, slicePerm, 0, d))
                    {
                        if (_stop) return true;
                        SaveSolution(d);
                        if (GoodEnough()) _stop = true;
                        return _stop;
                    }
                    if (_stop) return true;
                }
                return false;
            }

            private static int Phase2Bound(int corners, int edges, int slicePerm)
            {
                return Math.Max(CornerSlicePrun[corners * SlicePermCount + slicePerm], EdgeSlicePrun[edges * SlicePermCount + slicePerm]);
            }

            /// <summary>Returns true when a solution was found (or the search must stop: check _stop).</summary>
            private bool Phase2(int corners, int edges, int slicePerm, int depth, int togo)
            {
                if (togo == 0) return true; // bound 0: corners, U/D edges and slice edges are all solved

                int last = depth > 0 ? _path2[depth - 1] : _length1 > 0 ? _path1[_length1 - 1] : -1;
                for (int j = 0; j < Phase2MoveCount; j++)
                {
                    int m = Phase2Moves[j];
                    if (Redundant(last, m)) continue;

                    int nc = CornerPermMove[corners * Phase2MoveCount + j];
                    int ne = UDEdgePermMove[edges * Phase2MoveCount + j];
                    int ns = SlicePermMove[slicePerm * Phase2MoveCount + j];
                    if (Phase2Bound(nc, ne, ns) > togo - 1) continue;

                    _path2[depth] = m;
                    if (Phase2(nc, ne, ns, depth + 1, togo - 1)) return true;
                    if (TimeUp()) return true;
                }
                return false;
            }

            private void SaveSolution(int length2)
            {
                Array.Copy(_path1, _best, _length1);
                Array.Copy(_path2, 0, _best, _length1, length2);
                bool first = _bestLength == NoSolution;
                _bestLength = _length1 + length2;
                if (first)
                {
                    long now = Stopwatch.GetTimestamp();
                    _softDeadline = _improveTicks > long.MaxValue - now ? long.MaxValue : now + _improveTicks;
                }
            }

            private bool GoodEnough() => _bestLength <= TargetLength && _length1 >= EarlyStopPhase1Depth;

            private bool TimeUp()
            {
                if (_stop) return true;
                if ((++_nodes & 1023) != 0) return false;
                long now = Stopwatch.GetTimestamp();
                if (now > _deadline || now > _softDeadline) _stop = true;
                return _stop;
            }

            /// <summary>Same face twice in a row, or opposite faces in the non-canonical order (they commute).</summary>
            private static bool Redundant(int last, int m)
            {
                if (last < 0) return false;
                int f = m / 3, lf = last / 3;
                return f == lf || f == lf - 3;
            }

            private static bool IsPhase2Move(int m)
            {
                int f = m / 3;
                return f == 0 || f == 3 || m % 3 == 1; // any U or D turn, or a half turn
            }
        }
    }
}
