namespace Rubik.Core
{
    /// <summary>
    /// Грані куба. Порядок збігається з Kociemba-фацетами: U R F D L B.
    /// Значення грані одночасно є "кольором" наліпки в розв'язаному стані.
    /// </summary>
    public enum Face : byte { U = 0, R = 1, F = 2, D = 3, L = 4, B = 5 }

    /// <summary>
    /// Осі куба. Шар 0 кожної осі прилягає до "додатної" грані:
    /// X -> R, Y -> U, Z -> F. Шар N-1 прилягає до протилежної (L, D, B).
    /// </summary>
    public enum Axis : byte { X = 0, Y = 1, Z = 2 }
}
