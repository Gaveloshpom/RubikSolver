using Rubik.Core;
using Rubik.Solver3x3;
using System;
using System.Collections.Generic;

namespace Rubik.Cli
{
    internal static class Program
    {
        private const string Usage =
@"Usage: rubik [-n SIZE] [-s LENGTH [--seed SEED]] [-m ""MOVES""] [--solve]

  -n SIZE      cube size (default 3, max " + "100" + @")
  -s LENGTH    apply a random scramble of LENGTH moves
  --seed SEED  seed for the scramble (default: random)
  -m ""MOVES""   apply moves in standard notation, e.g. ""R U R' U' 2R 3Rw x""
  --solve      solve the cube (3x3 only) with the layer-by-layer solver";

        private static int Main(string[] args)
        {
            int n = 3, scrambleLength = 5, seed = Environment.TickCount;
            string moves = null;
            bool solve = true;
            bool useMES = true;

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "-n": n = int.Parse(args[++i]); break;
                        case "-s": scrambleLength = int.Parse(args[++i]); break;
                        case "--seed": seed = int.Parse(args[++i]); break;
                        case "-m": moves = args[++i]; break;
                        case "--solve": solve = true; break;
                        case "--mess": useMES = true; break;
                        default: Console.WriteLine(Usage); return args[i] == "-h" ? 0 : 1;
                    }
                }

                var cube = new Cube(n);

                Print(cube);

                if (scrambleLength > 0)
                {
                    var scramble = Scrambler.Generate(n, scrambleLength, seed);
                    Console.WriteLine($"Scramble (seed {seed}): {Notation.Format(scramble, n, useMES)}");
                    cube.Apply(scramble);
                }
                if (moves != null) cube.Apply(moves);

                Print(cube);
                Console.WriteLine();
                Console.WriteLine($"Size {n}, solved: {cube.IsSolved}");

                if (solve)
                {
                    if (n != 3)
                    {
                        Console.Error.WriteLine("--solve currently only supports 3x3 cubes.");
                        return 1;
                    }

                    // 1. Initialize the pruning tables
                    PruningTables.Initialize(new Cube(3));

                    List<Move> solution = null;

                    // 2. Try the two-phase search first (with the tables IDA* handles 12-14 moves instantly)
                    Console.WriteLine("Searching for optimal solution...");
                    solution = KociembaSolver.Solve(cube);

                    // 3. If no solution was found, fall back to the layer-by-layer solver
                    if (solution == null)
                    {
                        Console.WriteLine("Cube is too scrambled for short search, falling back to Layer-by-Layer...");
                        solution = LayerByLayerSolver.Solve(cube); // the original method
                    }

                    //var solution = LayerByLayerSolver.Solve(cube);
                    var check = cube.Clone();
                    check.Apply(solution);
                    Console.WriteLine();
                    Console.WriteLine($"Solution ({solution.Count} moves): {Notation.Format(solution, n, useMES)}");
                    Console.WriteLine($"Verified solved: {check.IsSolved}");
                }

                return 0;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is IndexOutOfRangeException)
            {
                Console.Error.WriteLine(e is IndexOutOfRangeException ? "Missing option value." : e.Message);
                Console.Error.WriteLine(Usage);
                return 1;
            }
        }

        private static void Print(Cube cube)
        {
            var previous = Console.ForegroundColor;
            foreach (char ch in cube.ToNetString())
            {
                switch (ch)
                {
                    case 'U': Console.ForegroundColor = ConsoleColor.White; break;
                    case 'R': Console.ForegroundColor = ConsoleColor.Red; break;
                    case 'F': Console.ForegroundColor = ConsoleColor.Green; break;
                    case 'D': Console.ForegroundColor = ConsoleColor.Yellow; break;
                    case 'L': Console.ForegroundColor = ConsoleColor.DarkYellow; break;
                    case 'B': Console.ForegroundColor = ConsoleColor.Blue; break;
                    default: Console.ForegroundColor = previous; break;
                }
                Console.Write(ch);
            }
            Console.ForegroundColor = previous;
            Console.WriteLine();
        }
    }
}
