using Rubik.Core;

namespace Rubik.Solver3x3
{
    /// <summary>
    /// Kept for existing callers: warms up the tables of <see cref="KociembaSolver"/>.
    /// The solver builds its tables by itself on first use, so calling this is optional.
    /// </summary>
    public static class PruningTables
    {
        public static bool IsInitialized { get; private set; }

        /// <param name="solvedCube">Ignored (the tables are always built for a standard 3x3 cube).</param>
        public static void Initialize(Cube solvedCube)
        {
            KociembaSolver.Initialize();
            IsInitialized = true;
        }
    }
}
