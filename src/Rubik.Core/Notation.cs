using System;
using System.Collections.Generic;
using System.Text;

namespace Rubik.Core
{
    /// <summary>
    /// Парсер і форматер нотації (токени розділяються пробілами).
    ///
    ///   R  U  F  L  D  B     зовнішній шар
    ///   2R  3D               n-й шар від грані (зріз)
    ///   Rw  3Rw  (або r)     широкий хід: 2 або n шарів від грані
    ///   x  y  z              поворот усього куба (за напрямком R, U, F)
    ///   M  E  S              середній шар для непарного N (за напрямком L, D, F)
    ///   суфікси              ' = проти годинникової, 2 = півоберт, 2' = 2
    /// </summary>
    public static class Notation
    {
        public static List<Move> Parse(string text, int size)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var result = new List<Move>();
            foreach (var token in text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                result.Add(ParseToken(token, size));
            return result;
        }

        private static Move ParseToken(string tok, int size)
        {
            int i = 0, prefix = 0;
            bool hasPrefix = false;
            while (i < tok.Length && tok[i] >= '0' && tok[i] <= '9')
            {
                prefix = prefix * 10 + (tok[i] - '0');
                if (prefix > 100000) throw Bad(tok, "number is too large");
                hasPrefix = true;
                i++;
            }
            if (i >= tok.Length) throw Bad(tok, "missing move letter");

            char c = tok[i++];
            bool wide = false;
            if (i < tok.Length && tok[i] == 'w') { wide = true; i++; }

            int turns = 1;
            if (i < tok.Length && tok[i] == '2') { turns = 2; i++; }
            if (i < tok.Length && tok[i] == '\'') { turns = turns == 2 ? 2 : 3; i++; }
            if (i != tok.Length) throw Bad(tok, "unexpected characters");

            // повороти всього куба
            if (c == 'x' || c == 'y' || c == 'z')
            {
                if (hasPrefix || wide) throw Bad(tok, "rotations take no prefix or 'w'");
                return new Move((Axis)(c - 'x'), 0, size - 1, turns);
            }

            // середні шари (непарні N)
            if (c == 'M' || c == 'E' || c == 'S')
            {
                if (hasPrefix || wide) throw Bad(tok, "slice moves take no prefix or 'w'");
                if (size % 2 == 0) throw Bad(tok, "M/E/S exist only for odd N; use numbered layers like 2R");
                int mid = (size - 1) / 2;
                if (c == 'M') return new Move(Axis.X, mid, mid, 4 - turns);   // як L
                if (c == 'E') return new Move(Axis.Y, mid, mid, 4 - turns);   // як D
                return new Move(Axis.Z, mid, mid, turns);                     // як F
            }

            // маленькі літери = широкі ходи
            if (c == 'u' || c == 'd' || c == 'r' || c == 'l' || c == 'f' || c == 'b')
            {
                wide = true;
                c = char.ToUpperInvariant(c);
            }

            Axis axis;
            bool positive;
            switch (c)
            {
                case 'R': axis = Axis.X; positive = true; break;
                case 'L': axis = Axis.X; positive = false; break;
                case 'U': axis = Axis.Y; positive = true; break;
                case 'D': axis = Axis.Y; positive = false; break;
                case 'F': axis = Axis.Z; positive = true; break;
                case 'B': axis = Axis.Z; positive = false; break;
                default: throw Bad(tok, $"unknown move letter '{c}'");
            }

            int k = hasPrefix ? prefix : (wide ? 2 : 1);
            if (k < 1 || k > size) throw Bad(tok, $"layer count {k} is outside 1..{size}");

            // глибини від грані: широкий = [0..k-1], одиночний = [k-1..k-1]
            int lo = wide ? 0 : k - 1;
            int hi = k - 1;

            return positive
                ? new Move(axis, lo, hi, turns)
                : new Move(axis, size - 1 - hi, size - 1 - lo, 4 - turns);
        }

        private static FormatException Bad(string tok, string why) =>
            new FormatException($"Invalid move '{tok}': {why}.");

        // ---------------------------------------------------------------- форматування

        /// <param name="useMES">Якщо true, замінює центральні шари (напр. 2R) на M, E, S (з правильними напрямками).</param>
        public static string Format(IEnumerable<Move> moves, int size, bool useMES = false)
        {
            var sb = new StringBuilder();
            foreach (var m in moves)
            {
                if (m.IsIdentity) continue;
                if (m.LayerTo >= size) throw new ArgumentOutOfRangeException(nameof(moves), "Move does not fit this cube size.");

                if (m.LayerFrom == 0 && m.LayerTo == size - 1)
                {
                    Append(sb, "xyz"[(int)m.Axis].ToString(), m.Turns);
                }
                else if (m.LayerFrom == 0)
                {
                    Append(sb, Prefixed(m.LayerCount, "RUF"[(int)m.Axis], true), m.Turns);
                }
                else if (m.LayerTo == size - 1)
                {
                    Append(sb, Prefixed(m.LayerCount, "LDB"[(int)m.Axis], true), 4 - m.Turns);
                }
                else
                {
                    // внутрішній діапазон: розкладаємо на одиночні шари
                    for (int layer = m.LayerFrom; layer <= m.LayerTo; layer++)
                    {
                        // Якщо це центральний шар непарного куба і увімкнена опція useMES
                        if (useMES && size % 2 != 0 && layer == (size - 1) / 2)
                        {
                            if (m.Axis == Axis.X) // M слідує за L (негативний напрямок X)
                                Append(sb, "M", 4 - m.Turns);
                            else if (m.Axis == Axis.Y) // E слідує за D (негативний напрямок Y)
                                Append(sb, "E", 4 - m.Turns);
                            else // S слідує за F (позитивний напрямок Z)
                                Append(sb, "S", m.Turns);
                        }
                        else
                        {
                            // звичайний запис (напр. "2R")
                            Append(sb, (layer + 1) + "RUF"[(int)m.Axis].ToString(), m.Turns);
                        }
                    }
                }
            }
            return sb.ToString();
        }

        private static string Prefixed(int count, char letter, bool wide)
        {
            if (count == 1) return letter.ToString();
            return (count == 2 ? "" : count.ToString()) + letter + (wide ? "w" : "");
        }

        private static void Append(StringBuilder sb, string token, int turns)
        {
            turns = ((turns % 4) + 4) % 4;
            if (turns == 0) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(token);
            if (turns == 2) sb.Append('2');
            else if (turns == 3) sb.Append('\'');
        }
    }
}
