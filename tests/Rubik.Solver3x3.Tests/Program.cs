using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Rubik.Core;
using Rubik.Solver3x3;

// Mass-scramble regression test for LayerByLayerSolver. This is the same style of test that found
// (and, after fixes, now catches regressions of) every bug described in the solver's development
// notes: middle-slice scrambles that permute face centres, edges/corners stuck in the wrong slot,
// pieces that are in the right slot but flipped, etc. A single hand-picked scramble is not a
// meaningful test for a cube solver — only broad, varied coverage is.

int failures = 0;
int total = 0;
var moveCounts = new List<int>();
var sw = Stopwatch.StartNew();

void Check(int size, int scrambleLength, int seed)
{
    total++;
    var cube = new Cube(size);
    cube.Apply(Scrambler.Generate(size, scrambleLength, seed));
    var before = cube.Clone();
    try
    {
        var solution = LayerByLayerSolver.Solve(cube);
        var check = before.Clone();
        check.Apply(solution);
        if (!check.IsSolved)
        {
            failures++;
            Console.WriteLine($"FAIL size={size} len={scrambleLength} seed={seed}: solver returned a sequence that does not solve the cube");
        }
        else
        {
            moveCounts.Add(solution.Count);
        }
    }
    catch (Exception e)
    {
        failures++;
        Console.WriteLine($"FAIL size={size} len={scrambleLength} seed={seed}: {e.GetType().Name}: {e.Message}");
    }
}

// Scramble lengths from "already solved" up to long, each with many seeds — this is what actually
// exercises rare cases (a piece landing exactly in its own slot but flipped, middle-slice moves
// permuting centres, etc.) that a single example would almost never hit.
foreach (var len in new[] { 0, 1, 2, 3, 5, 8, 15, 25, 50, 100 })
    for (int seed = 0; seed < 100; seed++)
        Check(3, len, seed * 97 + len);

var lblCounts = moveCounts.ToList();
moveCounts.Clear();

// KociembaSolver: random 20-move scrambles must be solved fast and short; the time limit is generous
// so that a slow regression shows up as a time report rather than as a failure on a busy machine.
var kociembaTimes = new List<double>();
var kociembaLengths = new List<int>();

void CheckKociemba(string name, Cube cube, int maxLength)
{
    total++;
    var t = Stopwatch.StartNew();
    var solution = KociembaSolver.Solve(cube.Clone(), TimeSpan.FromSeconds(10));
    t.Stop();
    if (solution == null)
    {
        failures++;
        Console.WriteLine($"FAIL kociemba {name}: no solution");
        return;
    }
    var check = cube.Clone();
    check.Apply(solution);
    if (!check.IsSolved || solution.Count > maxLength)
    {
        failures++;
        Console.WriteLine($"FAIL kociemba {name}: solved={check.IsSolved}, length={solution.Count}");
        return;
    }
    kociembaTimes.Add(t.Elapsed.TotalMilliseconds);
    kociembaLengths.Add(solution.Count);
}

var tableTimer = Stopwatch.StartNew();
KociembaSolver.Initialize();
long tableMs = tableTimer.ElapsedMilliseconds;

for (int seed = 0; seed < 200; seed++)
{
    var cube = new Cube(3);
    cube.Apply(Scrambler.Generate(3, 20, seed));
    CheckKociemba($"len=20 seed={seed}", cube, 30);
}
var randomTimes = kociembaTimes.OrderBy(x => x).ToList();
var randomLengths = kociembaLengths.ToList();

// Short scrambles: the solution must not be longer than the scramble.
foreach (var len in new[] { 0, 1, 2, 3, 4, 5, 6 })
    for (int seed = 0; seed < 30; seed++)
    {
        var cube = new Cube(3);
        cube.Apply(Scrambler.Generate(3, len, seed * 31 + len));
        CheckKociemba($"len={len} seed={seed}", cube, len);
    }

// A cube held in another orientation (centres moved) is solved with outer-face moves too.
{
    var cube = new Cube(3);
    cube.Apply("R U F' L2 D B' x y' z2");
    CheckKociemba("rotated", cube, 30);
}

// Impossible cubes: null, quickly.
void ExpectKociembaNull(string name, byte[] stickers)
{
    total++;
    var t = Stopwatch.StartNew();
    var solution = KociembaSolver.Solve(Cube.FromStickers(3, stickers));
    if (solution != null || t.ElapsedMilliseconds > 1000)
    {
        failures++;
        Console.WriteLine($"FAIL kociemba {name}: expected null quickly, got {(solution == null ? "null" : solution.Count + " moves")} in {t.ElapsedMilliseconds} ms");
    }
}
{
    var cube = new Cube(3);
    cube.Apply(Scrambler.Generate(3, 20, 12345));
    var s = cube.Stickers.ToArray();
    var flipped = (byte[])s.Clone();
    (flipped[7], flipped[19]) = (flipped[19], flipped[7]);
    ExpectKociembaNull("flipped edge", flipped);
    var twisted = (byte[])s.Clone();
    (twisted[8], twisted[9], twisted[20]) = (twisted[20], twisted[8], twisted[9]);
    ExpectKociembaNull("twisted corner", twisted);
    var swapped = (byte[])s.Clone();
    (swapped[7], swapped[5]) = (swapped[5], swapped[7]);
    (swapped[19], swapped[10]) = (swapped[10], swapped[19]);
    ExpectKociembaNull("swapped edges", swapped);
}

// A zero time limit must not throw (null or a valid solution are both fine).
{
    total++;
    var cube = new Cube(3);
    cube.Apply(Scrambler.Generate(3, 20, 7));
    var solution = KociembaSolver.Solve(cube.Clone(), TimeSpan.Zero);
    if (solution != null)
    {
        var check = cube.Clone();
        check.Apply(solution);
        if (!check.IsSolved) { failures++; Console.WriteLine("FAIL kociemba zero time limit: wrong solution"); }
    }
}

// Concurrent solves share the tables.
{
    var results = new bool[16];
    System.Threading.Tasks.Parallel.For(0, results.Length, i =>
    {
        var cube = new Cube(3);
        cube.Apply(Scrambler.Generate(3, 25, 1000 + i));
        var solution = KociembaSolver.Solve(cube.Clone());
        var check = cube.Clone();
        if (solution != null) check.Apply(solution);
        results[i] = solution != null && check.IsSolved;
    });
    for (int i = 0; i < results.Length; i++)
    {
        total++;
        if (!results[i]) { failures++; Console.WriteLine($"FAIL kociemba parallel {i}"); }
    }
}

// CubeValidator: every scramble is valid, while single "physically impossible" changes are not.
void ExpectValidation(string name, byte[] stickers, CubeValidationError expected)
{
    total++;
    var result = CubeValidator.Validate(stickers);
    if (result.Error != expected)
    {
        failures++;
        Console.WriteLine($"FAIL validator {name}: expected {expected}, got {result}");
    }
}

for (int seed = 0; seed < 200; seed++)
{
    var cube = new Cube(3);
    cube.Apply(Scrambler.Generate(3, 30, seed));
    var s = cube.Stickers.ToArray();
    ExpectValidation($"scramble {seed}", s, CubeValidationError.None);

    var flipped = (byte[])s.Clone();
    (flipped[7], flipped[19]) = (flipped[19], flipped[7]); // edge UF
    ExpectValidation($"flipped edge {seed}", flipped, CubeValidationError.FlippedEdge);

    var twisted = (byte[])s.Clone();
    (twisted[8], twisted[9], twisted[20]) = (twisted[20], twisted[8], twisted[9]); // corner URF
    ExpectValidation($"twisted corner {seed}", twisted, CubeValidationError.TwistedCorner);

    var swapped = (byte[])s.Clone();
    (swapped[7], swapped[5]) = (swapped[5], swapped[7]);   // edges UF <-> UR
    (swapped[19], swapped[10]) = (swapped[10], swapped[19]);
    ExpectValidation($"swapped edges {seed}", swapped, CubeValidationError.Parity);
}

var withEmpty = new Cube(3).Stickers.ToArray();
withEmpty[0] = 255;
ExpectValidation("empty sticker", withEmpty, CubeValidationError.NotFilled);

var wrongCount = new Cube(3).Stickers.ToArray();
wrongCount[0] = 1;
ExpectValidation("wrong count", wrongCount, CubeValidationError.WrongCount);

Console.WriteLine();
Console.WriteLine($"Solved: {total - failures}/{total} in {sw.ElapsedMilliseconds} ms");
if (lblCounts.Count > 0)
    Console.WriteLine($"Layer-by-layer length — min: {lblCounts.Min()}, avg: {lblCounts.Average():F1}, max: {lblCounts.Max()}");
if (randomTimes.Count > 0)
    Console.WriteLine($"Kociemba (200 x 20-move scrambles, tables {tableMs} ms) — time median: {randomTimes[randomTimes.Count / 2]:F0} ms, " +
                      $"max: {randomTimes[randomTimes.Count - 1]:F0} ms; length avg: {randomLengths.Average():F1}, max: {randomLengths.Max()}");

return failures == 0 ? 0 : 1;
