using System.Collections.Generic;

namespace Rubik.Core
{
    public static class MoveSequence
    {
        /// <summary>Обернена послідовність: ходи в зворотному порядку, кожен інвертований.</summary>
        public static List<Move> Invert(IReadOnlyList<Move> moves)
        {
            var result = new List<Move>(moves.Count);
            for (int i = moves.Count - 1; i >= 0; i--)
                result.Add(moves[i].Inverse());
            return result;
        }

        /// <summary>
        /// Зливає сусідні ходи однієї осі й одного діапазону шарів (R R -> R2, R R' -> нічого)
        /// і прибирає одиничні ходи. Каскадно: R U U' R' -> порожня послідовність.
        /// </summary>
        public static List<Move> Simplify(IEnumerable<Move> moves)
        {
            var result = new List<Move>();
            foreach (var m in moves)
            {
                if (m.IsIdentity) continue;

                if (result.Count > 0)
                {
                    var last = result[result.Count - 1];
                    if (last.Axis == m.Axis && last.LayerFrom == m.LayerFrom && last.LayerTo == m.LayerTo)
                    {
                        int turns = (last.Turns + m.Turns) % 4;
                        result.RemoveAt(result.Count - 1);
                        if (turns != 0)
                            result.Add(new Move(m.Axis, m.LayerFrom, m.LayerTo, turns));
                        continue;
                    }
                }
                result.Add(m);
            }
            return result;
        }
    }
}
