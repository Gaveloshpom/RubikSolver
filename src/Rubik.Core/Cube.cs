using System;
using System.Collections.Generic;
using System.Text;

namespace Rubik.Core
{
    /// <summary>
    /// NxN cube stored as stickers: one flat byte[6*N*N].
    /// Index = face * N*N + row * N + col. Value = color (0..5, the face index in the solved cube).
    ///
    /// Net (as in Kociemba):
    ///         U
    ///     L   F   R   B
    ///         D
    /// U: row 0 is next to face B; D: row 0 is next to face F;
    /// side faces: row 0 at the top, column 0 on the left (looking at the face from outside).
    ///
    /// Moves are defined by 3D geometry. Every sticker has integer coordinates
    /// (x right, y up, z towards the viewer), doubled and centred, so a 90° turn is an exact
    /// integer operation with no rounding and no hand-written permutation tables. A fast cycle-based
    /// version can be added later and checked against this one as the reference.
    /// </summary>
    public sealed class Cube : IEquatable<Cube>
    {
        /// <summary>Size limit for v1. It will be raised for v2 (giant cubes) together with the renderer.</summary>
        public const int MaxSize = 100;

        private readonly int _n;
        private readonly int _n2;
        private readonly byte[] _s;

        // Work buffers (so a move does not allocate): indices of the affected stickers and their values.
        private readonly int[] _idx;
        private readonly byte[] _val;
        private readonly int[] _p = new int[3];

        public Cube(int size)
        {
            if (size < 1 || size > MaxSize)
                throw new ArgumentOutOfRangeException(nameof(size), $"Size must be in 1..{MaxSize}.");

            _n = size;
            _n2 = size * size;
            _s = new byte[6 * _n2];
            int work = 2 * _n2 + 4 * _n; // with headroom: a face + the ring (both faces for N=1)
            _idx = new int[work];
            _val = new byte[work];
            Reset();
        }

        /// <summary>
        /// Cube from given stickers (6*N*N values 0..5, same order as <see cref="Stickers"/>).
        /// Checks only the length and the range; whether the cube is solvable is up to the solver.
        /// </summary>
        public static Cube FromStickers(int size, ReadOnlySpan<byte> stickers)
        {
            var c = new Cube(size);
            if (stickers.Length != c._s.Length)
                throw new ArgumentException($"Expected {c._s.Length} stickers, got {stickers.Length}.", nameof(stickers));
            for (int i = 0; i < stickers.Length; i++)
                if (stickers[i] > 5)
                    throw new ArgumentException($"Sticker {i} has invalid color {stickers[i]}.", nameof(stickers));
            stickers.CopyTo(c._s);
            return c;
        }

        public int Size => _n;

        /// <summary>Raw read-only stickers (for rendering, solvers, tests).</summary>
        public ReadOnlySpan<byte> Stickers => _s;

        public byte this[Face face, int row, int col]
        {
            get
            {
                if ((uint)row >= (uint)_n) throw new ArgumentOutOfRangeException(nameof(row));
                if ((uint)col >= (uint)_n) throw new ArgumentOutOfRangeException(nameof(col));
                return _s[(int)face * _n2 + row * _n + col];
            }
        }

        public void Reset()
        {
            for (int f = 0; f < 6; f++)
                Array.Fill(_s, (byte)f, f * _n2, _n2);
        }

        public Cube Clone()
        {
            var c = new Cube(_n);
            Array.Copy(_s, c._s, _s.Length);
            return c;
        }

        /// <summary>
        /// Solved: every face has one color and all six colors are different.
        /// This also works for even N (no fixed centres) and for a cube rotated in space.
        /// </summary>
        public bool IsSolved
        {
            get
            {
                int seen = 0;
                for (int f = 0; f < 6; f++)
                {
                    int start = f * _n2;
                    byte c = _s[start];
                    for (int i = 1; i < _n2; i++)
                        if (_s[start + i] != c) return false;

                    int bit = 1 << c;
                    if ((seen & bit) != 0) return false;
                    seen |= bit;
                }
                return true;
            }
        }

        // ---------------------------------------------------------------- moves

        public void Apply(Move move)
        {
            if (move.LayerTo >= _n)
                throw new ArgumentOutOfRangeException(nameof(move), "Layer index is outside the cube.");
            if (move.IsIdentity) return;

            for (int layer = move.LayerFrom; layer <= move.LayerTo; layer++)
                TurnLayer((int)move.Axis, layer, move.Turns);
        }

        public void Apply(IEnumerable<Move> moves)
        {
            foreach (var m in moves) Apply(m);
        }

        /// <summary>Applies notation, e.g. "R U R' U' 2R 3Rw2 x". See <see cref="Notation"/>.</summary>
        public void Apply(string notation) => Apply(Notation.Parse(notation, _n));

        /// <summary>
        /// Turns one layer: axis, layer (0 = next to the positive face), turns = 1..3 quarter turns.
        /// A layer = a ring of 4N stickers + (for outer layers) the whole N² face.
        /// </summary>
        private void TurnLayer(int axis, int layer, int turns)
        {
            int n = _n, m = n - 1;
            int v = m - 2 * layer;            // coordinate of this layer's cubie centres along the axis
            int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
            var p = _p;
            int cnt = 0;

            // 1) the ring: 4 side faces, N stickers each in this layer
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? a1 : a2;    // axis on which the sticker lies on the surface (=±N)
                int other = s == 0 ? a2 : a1;   // axis along which the strip runs
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    for (int j = 0; j < n; j++)
                    {
                        p[axis] = v;
                        p[side] = sign * n;
                        p[other] = 2 * j - m;
                        _idx[cnt++] = ToIndex(p[0], p[1], p[2]);
                    }
                }
            }

            // 2) the face itself if the layer is an outer one (both faces for N=1)
            if (layer == 0) cnt = AddFace(axis, n, cnt);
            if (layer == m) cnt = AddFace(axis, -n, cnt);

            // 3) move the values around
            for (int i = 0; i < cnt; i++) _val[i] = _s[_idx[i]];

            for (int i = 0; i < cnt; i++)
            {
                ToPoint(_idx[i], out int x, out int y, out int z);
                for (int t = 0; t < turns; t++) RotateOnce(axis, ref x, ref y, ref z);
                _s[ToIndex(x, y, z)] = _val[i];
            }
        }

        private int AddFace(int axis, int coord, int cnt)
        {
            int n = _n, m = n - 1;
            int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
            var p = _p;
            p[axis] = coord;
            for (int i = 0; i < n; i++)
            {
                p[a1] = 2 * i - m;
                for (int j = 0; j < n; j++)
                {
                    p[a2] = 2 * j - m;
                    _idx[cnt++] = ToIndex(p[0], p[1], p[2]);
                }
            }
            return cnt;
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>A clockwise quarter turn, looking from the positive side of the axis.</summary>
        private static void RotateOnce(int axis, ref int x, ref int y, ref int z)
        {
            int t;
            switch (axis)
            {
                case 0: t = y; y = z; z = -t; break;   // X: F -> U -> B -> D
                case 1: t = x; x = -z; z = t; break;   // Y: F -> L -> B -> R
                default: t = x; x = y; y = -t; break;  // Z: U -> R -> D -> L
            }
        }

        /// <summary>Point on the surface (one coordinate = ±N) -> sticker index.</summary>
        private int ToIndex(int x, int y, int z)
        {
            int n = _n, m = n - 1;
            int face, row, col;
            if (y == n)       { face = 0; col = (x + m) / 2; row = (z + m) / 2; }   // U
            else if (y == -n) { face = 3; col = (x + m) / 2; row = (m - z) / 2; }   // D
            else if (z == n)  { face = 2; col = (x + m) / 2; row = (m - y) / 2; }   // F
            else if (z == -n) { face = 5; col = (m - x) / 2; row = (m - y) / 2; }   // B
            else if (x == n)  { face = 1; col = (m - z) / 2; row = (m - y) / 2; }   // R
            else              { face = 4; col = (z + m) / 2; row = (m - y) / 2; }   // L
            return face * _n2 + row * n + col;
        }

        /// <summary>Sticker index -> point on the surface (inverse of ToIndex).</summary>
        private void ToPoint(int index, out int x, out int y, out int z)
        {
            int n = _n, m = n - 1;
            int face = index / _n2;
            int rem = index - face * _n2;
            int a = 2 * (rem % n) - m;   // from the column
            int b = 2 * (rem / n) - m;   // from the row
            switch (face)
            {
                case 0: x = a;  y = n;  z = b;  break;   // U
                case 1: x = n;  y = -b; z = -a; break;   // R
                case 2: x = a;  y = -b; z = n;  break;   // F
                case 3: x = a;  y = -n; z = -b; break;   // D
                case 4: x = -n; y = -b; z = a;  break;   // L
                default: x = -a; y = -b; z = -n; break;  // B
            }
        }

        // ---------------------------------------------------------------- equality / output

        public bool Equals(Cube other) =>
            other != null && other._n == _n && _s.AsSpan().SequenceEqual(other._s);

        public override bool Equals(object obj) => Equals(obj as Cube);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = _n;
                for (int i = 0; i < _s.Length; i++) h = h * 31 + _s[i];
                return h;
            }
        }

        /// <summary>Text net (URFDLB characters), lines separated by '\n'.</summary>
        public string ToNetString()
        {
            const string letters = "URFDLB";
            var sb = new StringBuilder();
            string pad = new string(' ', _n + 1);

            void FaceRow(Face f, int row)
            {
                for (int c = 0; c < _n; c++) sb.Append(letters[_s[(int)f * _n2 + row * _n + c]]);
            }

            for (int r = 0; r < _n; r++)
            {
                sb.Append(pad);
                FaceRow(Face.U, r);
                sb.Append('\n');
            }
            for (int r = 0; r < _n; r++)
            {
                FaceRow(Face.L, r); sb.Append(' ');
                FaceRow(Face.F, r); sb.Append(' ');
                FaceRow(Face.R, r); sb.Append(' ');
                FaceRow(Face.B, r);
                sb.Append('\n');
            }
            for (int r = 0; r < _n; r++)
            {
                sb.Append(pad);
                FaceRow(Face.D, r);
                if (r < _n - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        public override string ToString() => ToNetString();
    }
}
