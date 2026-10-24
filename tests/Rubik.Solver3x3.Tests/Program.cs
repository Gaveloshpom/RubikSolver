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
if (moveCounts.Count > 0)
    Console.WriteLine($"Solution length — min: {moveCounts.Min()}, avg: {moveCounts.Average():F1}, max: {moveCounts.Max()}");

return failures == 0 ? 0 : 1;
