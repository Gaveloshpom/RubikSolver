# Rubik Solver

Enter your Rubik's cube (3x3) and get a step-by-step solution on an animated 3D cube.
Under the hood: an NxN Rubik's cube engine in C#, designed to scale to bigger cubes.

- **Windows:** download `RubikSolver-1.0.0-win64.zip` from [Releases](https://github.com/Gaveloshpom/RubikSolver/releases), unzip it and run `RubikSolver.exe`. No install needed.
- **Web:** <https://gaveloshpom.github.io/RubikSolver/> (desktop browser with a mouse and keyboard).

## App features

- 3D cube: turn layers by dragging stickers with the mouse or with the keyboard (U R F D L B, x y z).
- Enter your own cube on the net or directly on the 3D cube; the app checks that the cube can actually be solved.
- Two-phase (Kociemba-style) solver with a layer-by-layer fallback.
- Solution player: step forward/back, play/pause, jump to any move, adjustable speed.
- English and Ukrainian UI, adjustable UI size.

The Unity app lives in `unity/RubikApp`; see [unity/README.md](unity/README.md) for building and publishing.

## Structure

```
src/Rubik.Core/        Core library (netstandard2.1, Unity compatible)
src/Rubik.Solver3x3/   3x3 solvers and the solvability check (netstandard2.1)
src/Rubik.Cli/         Console app: scramble, manual moves, --solve
unity/RubikApp/        Unity 6.6 (URP) app

tests/Rubik.Tests/              Real xunit tests for Core (need NuGet)
tests/Rubik.Tests.Offline/      The same Core tests without NuGet dependencies
tests/Rubik.Solver3x3.Tests/    Mass solver regression test + validator tests
```

`tests/Rubik.Tests.Offline` exists only because the environment this was first written in
had no access to nuget.org. Its `CubeTests.cs` and `NotationTests.cs` are literal copies of the
files in `tests/Rubik.Tests` (only the namespace differs). When building locally with NuGet access,
just use `tests/Rubik.Tests` and feel free to delete `Rubik.Tests.Offline`.

The projects without external dependencies (`Rubik.Core`, `Rubik.Solver3x3`, `Rubik.Cli`,
`Rubik.Tests.Offline`, `Rubik.Solver3x3.Tests`) have their own `nuget.config` with an empty source
list; that was only a convenience for offline builds and is safe to delete.

## Build and run

```bash
dotnet build
dotnet run --project src/Rubik.Cli -- -n 3 -s 20 --seed 42 --solve
dotnet test tests/Rubik.Tests                       # with NuGet access
dotnet run --project tests/Rubik.Tests.Offline      # without it
dotnet run --project tests/Rubik.Solver3x3.Tests -c Release   # mass solver test
```

For the Unity app, build the solver DLLs into the project first
(`powershell -ExecutionPolicy Bypass -File unity/sync-solver.ps1`), then open `unity/RubikApp`
in Unity 6000.6.3f1.

## Core design decisions

- **Stickers, not cubies.** `byte[6*N*N]`, index `face*N*N + row*N + col`.
  For N=1000 that is 6 MB, which is acceptable.
- **One `Move` type for everything**: `(Axis, LayerFrom, LayerTo, Turns)`. An outer face,
  an inner slice, a wide move (`3Rw`) and even a whole-cube rotation (`x/y/z`) are all
  special cases of the same layer range. That is the answer to "how to reuse the 3x3 code
  for NxN": there is no separate path, the whole engine is already written for any N.
- **Geometry as the single source of truth.** Every sticker has integer 3D coordinates
  (doubled and centred, so odd/even N never creates fractions). A 90° turn is an exact
  integer rotation matrix. This removes a whole class of "mixed up the row/column index
  on a particular face" bugs that are typical of hand-written permutation tables.
- **`IsSolved`** checks "every face has one color and all 6 colors differ" instead of
  comparing with a reference, which is correct for even N (no fixed centres) and for a cube
  rotated in space.
- **Notation** (`R U R' U' 2R 3Rw2 x M E S`) is parsed into a `List<Move>` and formatted back,
  suitable both for reading scrambles and for writing solutions.

### Covered by tests (`tests/Rubik.Tests` / `Rubik.Tests.Offline`, 79 tests)

50-move scramble + inverse → exact match with the solved cube (N=1..25);
4 quarter turns = identity; the directions of all 6 faces checked by hand against a physical
cube; order of the sexy move = 6; commuting and non-commuting layers; invariant number of
stickers of each color; notation parser (all formats + invalid tokens); `Simplify`/`Invert`
do not change the cube state.

## Rubik.Solver3x3

### Two-phase solver

`KociembaSolver.Solve(Cube cube)` reduces the cube to the subgroup G1 (phase 1, all face turns)
and then solves it with U, D and half turns of R, L, F, B (phase 2), using pruning tables from
`PruningTables`. It returns `null` if the cube is too scrambled for its depth limits; the app then
falls back to the layer-by-layer solver.

`CubeValidator.Validate` checks whether a sticker state is solvable at all: color counts,
valid corners and edges, corner twist, edge flip and permutation parity.

### Layer-by-layer (LBL) solver

`LayerByLayerSolver.Solve(Cube cube)` returns a `List<Move>` that solves a scrambled 3x3 cube.
Five phases, each one checked on the real cube rather than taken on faith:

1. **Cross** and **2. First-layer corners**: found by a small BFS (`ConstrainedSearch`)
   directly on the real cube with a restricted move set (to avoid a combinatorial explosion),
   with no pre-written formulas.
3. **Second-layer edges**: two classic insertion algorithms
   (`U R U' R' U' F' U F` and its mirror), tried with different U turns; only a variant that
   really gives the right result on the cube is accepted.
4. **OLL** (orientation of the last layer): `F R U R' U' F'` for edges, Sune
   (`R U R' U R U2 R'`) for corners, repeated with U turns in between until the goal is reached.
5. **PLL** (permutation of the last layer): Aa-perm (a pure 3-cycle of corners) and
   Ua-perm (a pure 3-cycle of edges); both were first found and verified by a search from the
   solved cube rather than taken from memory unchecked.

An important detail found through mass testing rather than a single example: the scrambler
may use middle-layer moves, which physically move the centre colors between faces (as on a real
cube). So the solver does not assume "D is always color 3"; it reads the **current** color of
every centre at the start (`ColorAt`) and looks for pieces by those colors, while face positions
(for writing moves) stay fixed separately.

### Covered by tests (`tests/Rubik.Solver3x3.Tests`)

1000 random scrambles of 0, 1, 2, 3, 5, 8, 15, 25, 50 and 100 moves (100 seeds each) are solved
and checked with `Cube.IsSolved` on the real engine, plus 800 validator checks (valid scrambles
and deliberately flipped, twisted or swapped pieces). The LBL solution length is not optimized
(about 64 moves on average, up to ~180), which is expected for a simple layer-by-layer method.

## Core performance (rough)

A 100x100 cube with 500 random moves: ~150 ms including .NET startup.
A move on an outer layer is O(N²) (because of the face itself), a slice is O(N).

## Roadmap

1. ~~3x3 algorithm (MVP)~~: done (layer-by-layer + two-phase).
2. Commutators for NxN, with parities (OLL/PLL parity) for even N.
3. ~~Unity: Core as a DLL, animation through a move queue~~: done, see `unity/`.
