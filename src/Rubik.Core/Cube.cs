using System;
using System.Collections.Generic;
using System.Text;

namespace Rubik.Core
{
    /// <summary>
    /// Куб NxN у вигляді наліпок (stickers): один плоский byte[6*N*N].
    /// Індекс = face * N*N + row * N + col. Значення = колір (0..5, номер грані розв'язаного куба).
    ///
    /// Розгортка (як у Kociemba):
    ///         U
    ///     L   F   R   B
    ///         D
    /// U: рядок 0 біля грані B; D: рядок 0 біля грані F;
    /// бічні грані: рядок 0 угорі, стовпець 0 ліворуч (якщо дивитися на грань ззовні).
    ///
    /// Джерело істини для ходів — 3D-геометрія. Кожна наліпка має цілі координати
    /// (x вправо, y вгору, z до глядача), подвоєні й центровані, тож поворот на 90° це точна
    /// цілочисельна операція без похибок і без ручних таблиць перестановок. Пізніше можна
    /// додати швидку цикл-версію і звіряти її з цією як з еталоном.
    /// </summary>
    public sealed class Cube : IEquatable<Cube>
    {
        /// <summary>Ліміт розміру для v1. Для v2 (гіга-куби) підніметься разом із рендером.</summary>
        public const int MaxSize = 100;

        private readonly int _n;
        private readonly int _n2;
        private readonly byte[] _s;

        // Робочі буфери (щоб хід не алокував): індекси зачеплених наліпок і їхні значення.
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
            int work = 2 * _n2 + 4 * _n; // з запасом: грань + кільце (для N=1 обидві грані)
            _idx = new int[work];
            _val = new byte[work];
            Reset();
        }

        /// <summary>
        /// Куб із готових наліпок (6*N*N значень 0..5, порядок як у <see cref="Stickers"/>).
        /// Перевіряє тільки довжину й діапазон; чи такий куб можна зібрати — справа солвера.
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

        /// <summary>Сирі наліпки тільки для читання (для рендера, солверів, тестів).</summary>
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
        /// Розв'язаний: кожна грань одноколірна і всі шість кольорів різні.
        /// Так працює й для парних N (немає нерухомих центрів) і для куба, повернутого в просторі.
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

        // ---------------------------------------------------------------- ходи

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

        /// <summary>Застосовує нотацію, напр. "R U R' U' 2R 3Rw2 x". Див. <see cref="Notation"/>.</summary>
        public void Apply(string notation) => Apply(Notation.Parse(notation, _n));

        /// <summary>
        /// Повертає один шар: axis, layer (0 = від додатної грані), turns = 1..3 чвертей.
        /// Шар = кільце з 4N наліпок + (для крайніх шарів) уся грань N².
        /// </summary>
        private void TurnLayer(int axis, int layer, int turns)
        {
            int n = _n, m = n - 1;
            int v = m - 2 * layer;            // координата центрів кубиків цього шару вздовж осі
            int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
            var p = _p;
            int cnt = 0;

            // 1) кільце: 4 бічні грані, по N наліпок у шарі
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? a1 : a2;    // вісь, на якій наліпка лежить на поверхні (=±N)
                int other = s == 0 ? a2 : a1;   // вісь, вздовж якої йде смужка
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

            // 2) сама грань, якщо шар крайній (для N=1 це обидві грані)
            if (layer == 0) cnt = AddFace(axis, n, cnt);
            if (layer == m) cnt = AddFace(axis, -n, cnt);

            // 3) циклічно переносимо значення
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

        // ---------------------------------------------------------------- геометрія

        /// <summary>Чверть оберту за годинниковою стрілкою, якщо дивитися з додатного боку осі.</summary>
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

        /// <summary>Точка на поверхні (одна координата = ±N) -> індекс наліпки.</summary>
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

        /// <summary>Індекс наліпки -> точка на поверхні (обернене до ToIndex).</summary>
        private void ToPoint(int index, out int x, out int y, out int z)
        {
            int n = _n, m = n - 1;
            int face = index / _n2;
            int rem = index - face * _n2;
            int a = 2 * (rem % n) - m;   // з колонки
            int b = 2 * (rem / n) - m;   // з рядка
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

        // ---------------------------------------------------------------- порівняння / вивід

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

        /// <summary>Текстова розгортка (символи URFDLB), рядки розділені '\n'.</summary>
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
