using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rubik.Core;

namespace Rubik.Solver3x3
{
    /// <summary>
    /// Beginner ("layer by layer") 3x3 solver: cross, first-layer corners, second-layer edges,
    /// orient last layer, permute last layer — solved one small goal at a time. Each goal is reached
    /// with <see cref="ConstrainedSearch"/>, which searches the real cube directly, so every step is
    /// verified against the real engine rather than relying on hand-copied algorithms.
    ///
    /// U/R/F/D/L/B below are fixed PHYSICAL POSITIONS (0..5, matching <see cref="Face"/>) — used to
    /// name slots and to generate move notation, which always refers to a position ("turn the R
    /// face"), never a colour. Which colour currently BELONGS at a position is a separate, dynamic
    /// thing: a scramble can include middle-slice moves, which legitimately swap face centres around
    /// (exactly like on a real cube), so "the D position's colour is always 3" is not a safe
    /// assumption. <see cref="ColorAt"/> is the position -> current-centre-colour lookup, rebuilt by
    /// <see cref="InitializeSlotMaps"/> at the start of every solve, and used everywhere a piece needs
    /// to be identified/matched by colour.
    /// </summary>
    public static class LayerByLayerSolver
    {
        private const byte U = 0, R = 1, F = 2, D = 3, L = 4, B = 5;

        /// <summary>Current centre colour at a fixed position, valid for the cube passed to the most
        /// recent <see cref="InitializeSlotMaps"/> call.</summary>
        private static byte[] ColorAt;

        private static void ComputeColorAt(Cube c) =>
            ColorAt = new[] { c[Face.U, 1, 1], c[Face.R, 1, 1], c[Face.F, 1, 1], c[Face.D, 1, 1], c[Face.L, 1, 1], c[Face.B, 1, 1] };

        /// <summary>When true, logs per-phase progress and timing to stderr. Useful while developing.</summary>
        public static bool Verbose { get; set; } = false;

        public static List<Move> Solve(Cube cube, int size = 3)
        {
            if (cube.Size != 3) throw new ArgumentException("LayerByLayerSolver only supports 3x3 cubes.");

            var (f2lMoves, placedEdges, placedCorners) = SolveFirstTwoLayers(cube);
            var work = cube.Clone();
            var solution = new List<Move>(f2lMoves);
            work.Apply(f2lMoves);
            void Do(List<Move> moves) { work.Apply(moves); solution.AddRange(moves); }
            void Log(string msg) { if (Verbose) Console.Error.WriteLine(msg); }
            Log($"Phases 1-3 (F2L): {solution.Count} moves");

            // --------------------------------------------------------------- Phase 4: orient last layer (OLL)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Do(OrientLastLayer(work, placedEdges, placedCorners));
            Log($"Phase 4 (OLL): {sw.ElapsedMilliseconds} ms, {solution.Count} moves so far");

            // --------------------------------------------------------------- Phase 5: permute last layer (PLL)
            sw.Restart();
            Do(PermuteLastLayer(work, placedEdges, placedCorners));
            Log($"Phase 5 (PLL): {sw.ElapsedMilliseconds} ms, {solution.Count} moves so far");

            //return solution;
            return MoveOptimizer.Optimize(solution);
        }

        /// <summary>Runs phases 1–3 only (cross + first-layer corners + second-layer edges). Exposed
        /// for testing OLL/PLL in isolation against realistic, F2L-solved starting positions.</summary>
        public static (List<Move> moves, List<(byte, byte)> edges, List<(byte, byte, byte)> corners) SolveFirstTwoLayers(Cube cube)
        {
            InitializeSlotMaps(cube);
            var work = cube.Clone();
            var solution = new List<Move>();
            void Do(List<Move> moves) { work.Apply(moves); solution.AddRange(moves); }

            var crossEdges = new (byte side, byte target)[] { (F, 2), (R, 1), (B, 5), (L, 4) };
            var placedEdges = new List<(byte a, byte b)>();
            foreach (var (_, side) in crossEdges)
            {
                var target = (D, side);
                var withTarget = new List<(byte, byte)>(placedEdges) { target };
                Do(ConstrainedSearch.Solve(work, 6, c => EdgeKey(c, withTarget), c => EdgesSolved(c, withTarget), ConstrainedSearch.LastLayerMoves));
                placedEdges.Add(target);
            }

            var cornerTargets = new (byte, byte, byte)[] { (D, F, R), (D, L, F), (D, B, L), (D, R, B) };
            var placedCorners = new List<(byte, byte, byte)>();
            var dCornerSlots = new HashSet<CornerSlot> { CornerSlot.DFR, CornerSlot.DLF, CornerSlot.DBL, CornerSlot.DRB };
            foreach (var target in cornerTargets)
            {
                var cloc0 = PieceFinder.FindCorner(work, Col(target.Item1), Col(target.Item2), Col(target.Item3));
                if (dCornerSlots.Contains(cloc0.Slot) && cloc0.Slot != CornerSlotFor(target.Item1, target.Item2, target.Item3))
                    Do(EjectCornerFromWrongDSlot(work, cloc0.Slot, placedEdges, placedCorners));

                var withTarget = new List<(byte, byte, byte)>(placedCorners) { target };
                Do(ConstrainedSearch.Solve(work, 8,
                    c => EdgeKey(c, placedEdges) + "|" + CornerKey(c, withTarget),
                    c => EdgesSolved(c, placedEdges) && CornersSolved(c, withTarget),
                    RestrictedMoves(target.Item2, target.Item3)));
                placedCorners.Add(target);
            }

            var secondLayerTargets = new (byte, byte)[] { (F, R), (F, L), (B, R), (B, L) };
            var midSlotFaces = new Dictionary<EdgeSlot, byte> { [EdgeSlot.FR] = F, [EdgeSlot.FL] = F, [EdgeSlot.BR] = B, [EdgeSlot.BL] = B };
            foreach (var target in secondLayerTargets)
            {
                var withTarget = new List<(byte, byte)>(placedEdges) { target };
                Do(InsertSecondLayerEdge(work, target, withTarget, placedCorners));
                placedEdges.Add(target);
            }

            return (solution, placedEdges, placedCorners);
        }

        // ------------------------------------------------------------ OLL: orient last layer
        //
        // Two well-known building-block algorithms, each verified above (empirically, not just from
        // memory) to preserve F2L: "F R U R' U' F'" orients the 4 U edges (flips them into a same-
        // colour cross on top), and Sune "R U R' U R U2 R'" cycles/twists U corners' orientation.
        // Both are tried at every U pre-rotation, and edge-orientation up to 2 times / corner-
        // orientation up to 3 times (those bounds cover every case for 4 pieces with 2, resp. 3,
        // orientation states) — the first combination that actually reaches the goal on the real cube
        // is used, so there is nothing here that depends on correctly recalling *which* rotation goes
        // with *which* case.

        private static List<Move> OrientLastLayer(Cube cube, List<(byte, byte)> edges, List<(byte, byte, byte)> corners)
        {
            var full = new List<Move>();
            var work = cube.Clone();

            bool UEdgesOriented(Cube c)
            {
                byte u = c[Face.U, 1, 1];
                return c[Face.U, 0, 1] == u && c[Face.U, 1, 0] == u && c[Face.U, 1, 2] == u && c[Face.U, 2, 1] == u;
            }
            bool UUniform(Cube c)
            {
                byte u = c[Face.U, 1, 1];
                for (int r = 0; r < 3; r++) for (int col = 0; col < 3; col++) if (c[Face.U, r, col] != u) return false;
                return true;
            }
            bool F2LOk(Cube c) => EdgesSolved(c, edges) && CornersSolved(c, corners);

            var edgeMoves = FindByRepeatedApplication(work, "F R U R' U' F'", maxApps: 3,
                c => F2LOk(c) && UEdgesOriented(c));
            work.Apply(edgeMoves);
            full.AddRange(edgeMoves);

            var cornerMoves = FindByRepeatedApplication(work, "R U R' U R U2 R'", maxApps: 3,
                c => F2LOk(c) && UUniform(c));
            work.Apply(cornerMoves);
            full.AddRange(cornerMoves);

            return full;
        }

        /// <summary>Tries U^r0 ALG U^r1 ALG ... for up to maxApps applications (each with an
        /// independent U pre-rotation), returning the first combination reaching <paramref
        /// name="goal"/>. Small and exhaustive rather than derived by hand — see class remarks.</summary>
        private static List<Move> FindByRepeatedApplication(Cube start, string alg, int maxApps, Func<Cube, bool> goal)
        {
            if (goal(start)) return new List<Move>();

            var algMoves = Notation.Parse(alg, 3);
            string[] rotations = { "", "U", "U2", "U'" };

            List<Move> TryDepth(int depth)
            {
                int[] idx = new int[depth];
                while (true)
                {
                    var candidate = new List<Move>();
                    var trial = start.Clone();
                    for (int i = 0; i < depth; i++)
                    {
                        var pre = Notation.Parse(rotations[idx[i]], 3);
                        candidate.AddRange(pre);
                        candidate.AddRange(algMoves);
                        trial.Apply(pre);
                        trial.Apply(algMoves);
                    }
                    if (goal(trial)) return candidate;

                    int p = depth - 1;
                    while (p >= 0 && ++idx[p] == 4) { idx[p] = 0; p--; }
                    if (p < 0) break;
                }
                return null;
            }

            for (int depth = 1; depth <= maxApps; depth++)
            {
                var found = TryDepth(depth);
                if (found != null) return found;
            }
            throw new InvalidOperationException($"Could not reach goal by repeating '{alg}' up to {maxApps} times.");
        }

        // ------------------------------------------------------------ PLL: permute last layer
        //
        // Aa-perm ("R' F R' B2 R F' R' B2 R2") is a pure 3-cycle of 3 last-layer CORNERS (verified
        // above to leave all edges and F2L untouched). Ua-perm ("R U' R U R U R U' R' U' R2") is a
        // pure 3-cycle of 3 last-layer EDGES. Together with U pre-rotations and their inverses, these
        // generate every even permutation of 4 objects, which is exactly the set of permutations a
        // legally-scrambled cube can present here — so trying a bounded number of applications always
        // terminates.

        private static List<Move> PermuteLastLayer(Cube cube, List<(byte, byte)> edges, List<(byte, byte, byte)> corners)
        {
            var full = new List<Move>();
            var work = cube.Clone();

            bool F2LOk(Cube c) => EdgesSolved(c, edges) && CornersSolved(c, corners);
            bool CornersInPlace(Cube c)
            {
                foreach (CornerSlot s in new[] { CornerSlot.URF, CornerSlot.UFL, CornerSlot.ULB, CornerSlot.UBR })
                {
                    var f = PieceMap.CornerFacelets[(int)s];
                    byte a = Col((byte)f[0].face), b = Col((byte)f[1].face), cc = Col((byte)f[2].face);
                    if (PieceFinder.FindCorner(c, a, b, cc).Slot != s) return false;
                }
                return true;
            }
            bool EdgesInPlace(Cube c)
            {
                foreach (EdgeSlot s in new[] { EdgeSlot.UF, EdgeSlot.UR, EdgeSlot.UB, EdgeSlot.UL })
                {
                    var f = PieceMap.EdgeFacelets[(int)s];
                    byte a = Col((byte)f[0].face), b = Col((byte)f[1].face);
                    if (PieceFinder.FindEdge(c, a, b).Slot != s) return false;
                }
                return true;
            }

            var cornerFix = FindByRepeatedApplication(work, "R' F R' B2 R F' R' B2 R2", maxApps: 3,
                c => F2LOk(c) && CornersInPlace(c));
            work.Apply(cornerFix);
            full.AddRange(cornerFix);

            var edgeFix = FindByRepeatedApplication(work, "R U' R U R U R U' R' U' R2", maxApps: 3,
                c => F2LOk(c) && CornersInPlace(c) && EdgesInPlace(c));
            work.Apply(edgeFix);
            full.AddRange(edgeFix);

            // Final AUF (align-U-face) if the layer is solved but rotated relative to the sides.
            foreach (var rot in new[] { "", "U", "U2", "U'" })
            {
                var trial = work.Clone();
                trial.Apply(rot);
                if (F2LOk(trial) && CornersInPlace(trial) && EdgesInPlace(trial))
                {
                    full.AddRange(Notation.Parse(rot, 3));
                    return full;
                }
            }
            return full;
        }

        // ------------------------------------------------------------ eject a misplaced D-layer corner
        //
        // A D-layer corner sitting in the WRONG slot needs to go to the U layer before the (2-face +
        // U) restricted corner search below can reach it. A double turn of one of its own faces does
        // NOT reliably do this (verified by testing many scrambles — see commit history); instead we
        // search for a short move sequence, restricted to that slot's own 2 faces + U, that moves the
        // piece out of every D-corner slot while preserving everything already placed.

        private static readonly CornerSlot[] DCornerSlotsArr =
            { CornerSlot.DFR, CornerSlot.DLF, CornerSlot.DBL, CornerSlot.DRB };

        private static List<Move> EjectCornerFromWrongDSlot(
            Cube cube, CornerSlot slot, List<(byte, byte)> placedEdges, List<(byte, byte, byte)> placedCorners)
        {
            var faces = PieceMap.CornerFacelets[(int)slot]; // [0]=U/D, [1] and [2]=the 2 side faces
            byte faceA = (byte)faces[1].face, faceB = (byte)faces[2].face;

            byte[] originalColors = PieceMap.ColorsAt(cube, PieceMap.CornerFacelets[(int)slot]);
            bool PieceLeftDLayer(Cube c)
            {
                var loc = PieceFinder.FindCorner(c, originalColors[0], originalColors[1], originalColors[2]);
                return Array.IndexOf(DCornerSlotsArr, loc.Slot) < 0;
            }

            return ConstrainedSearch.Solve(
                cube, maxDepth: 5,
                stateKey: c => EdgeKey(c, placedEdges) + "|" + CornerKey(c, placedCorners) + "|" +
                                PieceFinder.FindCorner(c, originalColors[0], originalColors[1], originalColors[2]).Slot,
                isGoal: c => PieceLeftDLayer(c) && EdgesSolved(c, placedEdges) && CornersSolved(c, placedCorners),
                moveSet: RestrictedMoves(faceA, faceB));
        }

        // ------------------------------------------------------------ second-layer edge insertion
        //
        // Classic beginner F2L edge inserts, tried in both "roles" (each of the target's two faces
        // playing the "main"/front-like role) and after each of the 4 possible U pre-rotations — the
        // one combination that actually reaches the goal (checked on the real cube) is used. This
        // keeps the reliability of empirical verification without the combinatorial blow-up of a
        // free BFS, because F/R-type moves briefly disturb already-placed D-layer corners and that
        // makes an undirected search explode (see development notes / commit history).

        private static List<Move> InsertSecondLayerEdge(
            Cube cube, (byte a, byte b) target, List<(byte, byte)> withTarget, List<(byte, byte, byte)> placedCorners)
        {
            string RightAlg(string m, string s) => $"U {s} U' {s}' U' {m}' U {m}";
            string LeftAlg(string m, string s) => $"U' {s}' U {s} U {m} U' {m}'";

            var midSlotFaces = new Dictionary<EdgeSlot, (byte main, byte side)>
            {
                [EdgeSlot.FR] = (F, R), [EdgeSlot.FL] = (F, L), [EdgeSlot.BR] = (B, R), [EdgeSlot.BL] = (B, L),
            };

            var loc0 = PieceFinder.FindEdge(cube, Col(target.a), Col(target.b));
            if (loc0.Slot == EdgeSlotFor(target.a, target.b) && loc0.PrimaryIndex == 0)
                return new List<Move>(); // already correctly placed — nothing to do

            // Stuck in a middle-layer slot that isn't correct yet (wrong slot, or right slot but
            // flipped): pop it up to the U layer by applying the INVERSE of a standard insertion
            // algorithm for THAT slot's own two faces. (A double turn of the face it sits under does
            // NOT do this for middle-layer edges — it only swaps it with the mirror-image slot, e.g.
            // BR<->BL — that was an earlier bug here, caught by testing many scrambles.)
            var prefix = new List<Move>();
            bool needsEject = midSlotFaces.ContainsKey(loc0.Slot) &&
                               (loc0.Slot != EdgeSlotFor(target.a, target.b) || loc0.PrimaryIndex != 0);
            if (needsEject)
            {
                var (m, s) = midSlotFaces[loc0.Slot];
                string ml = FaceLetter(m), sl = FaceLetter(s);
                bool found = false;
                foreach (var alg in new[] { RightAlg(ml, sl), LeftAlg(ml, sl) })
                {
                    var inverse = MoveSequence.Invert(Notation.Parse(alg, 3));
                    var trial = cube.Clone();
                    trial.Apply(inverse);
                    var s2 = PieceFinder.FindEdge(trial, Col(target.a), Col(target.b)).Slot;
                    if (s2 == EdgeSlot.UF || s2 == EdgeSlot.UR || s2 == EdgeSlot.UB || s2 == EdgeSlot.UL)
                    {
                        prefix.AddRange(inverse);
                        found = true;
                        break;
                    }
                }
                if (!found)
                    throw new InvalidOperationException($"Could not eject 2nd-layer edge out of slot {loc0.Slot} for target {target}.");
            }

            var baseCube = cube.Clone();
            baseCube.Apply(prefix);

            string mainLetter = FaceLetter(target.a), sideLetter = FaceLetter(target.b);
            var candidates = new List<string>();
            for (int r = 0; r <= 3; r++)
            {
                string pre = r == 0 ? "" : (r == 1 ? "U " : r == 2 ? "U2 " : "U' ");
                candidates.Add(pre + RightAlg(mainLetter, sideLetter));
                candidates.Add(pre + LeftAlg(mainLetter, sideLetter));
                candidates.Add(pre + RightAlg(sideLetter, mainLetter));
                candidates.Add(pre + LeftAlg(sideLetter, mainLetter));
            }

            foreach (var candidate in candidates)
            {
                var trial = baseCube.Clone();
                var parsed = Notation.Parse(candidate, 3);
                trial.Apply(parsed);
                if (EdgesSolved(trial, withTarget) && CornersSolved(trial, placedCorners))
                {
                    var full = new List<Move>(prefix);
                    full.AddRange(parsed);
                    return full;
                }
            }

            throw new InvalidOperationException(
                $"Could not insert 2nd-layer edge {target}. Located at slot={loc0.Slot}, primaryIndex={loc0.PrimaryIndex}, " +
                $"prefix={Notation.Format(prefix, 3)}, afterPrefixLoc={PieceFinder.FindEdge(baseCube, Col(target.a), Col(target.b)).Slot}");
        }

        // ------------------------------------------------------------ helpers

        private static readonly string[] FaceLetters = { "U", "R", "F", "D", "L", "B" };
        private static string FaceLetter(byte face) => FaceLetters[face];

        /// <summary>U (any amount) plus the two given faces (any amount) — enough for any standard
        /// insertion/orientation algorithm that targets a slot touching exactly those two faces, and
        /// small enough that a BFS over it stays fast.</summary>
        private static string[] RestrictedMoves(byte faceA, byte faceB)
        {
            string a = FaceLetter(faceA), b = FaceLetter(faceB);
            return new[] { "U", "U'", "U2", a, a + "'", a + "2", b, b + "'", b + "2" };
        }

        private static bool EdgesSolved(Cube c, List<(byte a, byte b)> targets)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var (a, b) = targets[i];
                var loc = PieceFinder.FindEdge(c, Col(a), Col(b));
                if (loc.PrimaryIndex != 0 || loc.Slot != EdgeSlotFor(a, b)) return false;
            }
            return true;
        }

        private static string EdgeKey(Cube c, List<(byte a, byte b)> targets)
        {
            var sb = new StringBuilder(targets.Count * 4);
            for (int i = 0; i < targets.Count; i++)
            {
                var (a, b) = targets[i];
                var loc = PieceFinder.FindEdge(c, Col(a), Col(b));
                sb.Append((int)loc.Slot).Append(':').Append(loc.PrimaryIndex).Append(';');
            }
            return sb.ToString();
        }

        private static bool CornersSolved(Cube c, List<(byte, byte, byte)> targets)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var (a, b, cc) = targets[i];
                var loc = PieceFinder.FindCorner(c, Col(a), Col(b), Col(cc));
                if (loc.PrimaryIndex != 0 || loc.Slot != CornerSlotFor(a, b, cc)) return false;
            }
            return true;
        }

        private static string CornerKey(Cube c, List<(byte, byte, byte)> targets)
        {
            var sb = new StringBuilder(targets.Count * 4);
            for (int i = 0; i < targets.Count; i++)
            {
                var (a, b, cc) = targets[i];
                var loc = PieceFinder.FindCorner(c, Col(a), Col(b), Col(cc));
                sb.Append((int)loc.Slot).Append(':').Append(loc.PrimaryIndex).Append(';');
            }
            return sb.ToString();
        }

        private static bool FirstTwoLayersSolved(Cube c, List<(byte a, byte b)> edges, List<(byte, byte, byte)> corners)
            => EdgesSolved(c, edges) && CornersSolved(c, corners);

        private static string FirstTwoLayersKey(Cube c, List<(byte a, byte b)> edges, List<(byte, byte, byte)> corners)
            => EdgeKey(c, edges) + "|" + CornerKey(c, corners);

        private static bool UFaceUniform(Cube c)
        {
            byte first = c[Face.U, 0, 0];
            for (int r = 0; r < 3; r++)
                for (int col = 0; col < 3; col++)
                    if (c[Face.U, r, col] != first) return false;
            return true;
        }

        private static string UFacePattern(Cube c)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < 3; r++)
                for (int col = 0; col < 3; col++)
                    sb.Append(c[Face.U, r, col]);
            return sb.ToString();
        }

        private static string FullStateKey(Cube c)
        {
            var sb = new StringBuilder();
            foreach (byte s in c.Stickers) sb.Append((char)('a' + s));
            return sb.ToString();
        }

        // Rebuilt per-solve by InitializeSlotMaps (see Solve/SolveFirstTwoLayers), because which
        // colour "belongs" at a slot depends on that cube's actual current centre colours, not on
        // the Face enum's fixed numbering.
        private static Dictionary<int, EdgeSlot> EdgeSlotByColors;
        private static Dictionary<int, CornerSlot> CornerSlotByColors;

        private static void InitializeSlotMaps(Cube cube)
        {
            ComputeColorAt(cube);
            byte ColorOf(Face f) => ColorAt[(int)f];

            EdgeSlotByColors = new Dictionary<int, EdgeSlot>();
            foreach (EdgeSlot slot in Enum.GetValues(typeof(EdgeSlot)))
            {
                var f = PieceMap.EdgeFacelets[(int)slot];
                byte id0 = ColorOf(f[0].face), id1 = ColorOf(f[1].face);
                EdgeSlotByColors[PairKey(id0, id1)] = slot;
                EdgeSlotByColors[PairKey(id1, id0)] = slot;
            }

            CornerSlotByColors = new Dictionary<int, CornerSlot>();
            foreach (CornerSlot slot in Enum.GetValues(typeof(CornerSlot)))
            {
                var f = PieceMap.CornerFacelets[(int)slot];
                byte[] ids = { ColorOf(f[0].face), ColorOf(f[1].face), ColorOf(f[2].face) };
                foreach (var p in Permute3(ids)) CornerSlotByColors[TripleKey(p[0], p[1], p[2])] = slot;
            }
        }

        private static IEnumerable<byte[]> Permute3(byte[] v)
        {
            yield return new[] { v[0], v[1], v[2] };
            yield return new[] { v[0], v[2], v[1] };
            yield return new[] { v[1], v[0], v[2] };
            yield return new[] { v[1], v[2], v[0] };
            yield return new[] { v[2], v[0], v[1] };
            yield return new[] { v[2], v[1], v[0] };
        }

        private static int PairKey(byte a, byte b) => a * 8 + b;
        private static int TripleKey(byte a, byte b, byte c) => a * 64 + b * 8 + c;

        /// <summary>Current colour at a fixed position (see <see cref="ColorAt"/>).</summary>
        private static byte Col(byte position) => ColorAt[position];

        private static EdgeSlot EdgeSlotFor(byte posA, byte posB) => EdgeSlotByColors[PairKey(Col(posA), Col(posB))];
        private static CornerSlot CornerSlotFor(byte posA, byte posB, byte posC) => CornerSlotByColors[TripleKey(Col(posA), Col(posB), Col(posC))];
    }
}
