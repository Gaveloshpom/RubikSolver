namespace Rubik.Core
{
    /// <summary>
    /// Cube faces. The order matches the Kociemba facelets: U R F D L B.
    /// A face value is also the "color" of its stickers in the solved state.
    /// </summary>
    public enum Face : byte { U = 0, R = 1, F = 2, D = 3, L = 4, B = 5 }

    /// <summary>
    /// Cube axes. Layer 0 of each axis is next to the "positive" face:
    /// X -> R, Y -> U, Z -> F. Layer N-1 is next to the opposite one (L, D, B).
    /// </summary>
    public enum Axis : byte { X = 0, Y = 1, Z = 2 }
}
