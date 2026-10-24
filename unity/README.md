# Rubik Solver — Unity app

Unity 6.6 (URP) app on top of `src/Rubik.Core` and `src/Rubik.Solver3x3`.

## First run after cloning

1. Build the solver DLLs into the project (repeat after every change in `src/`):
   ```
   powershell -ExecutionPolicy Bypass -File unity/sync-solver.ps1
   ```
2. Open `unity/RubikApp` in Unity Hub (editor 6000.6.3f1).
3. Open `Assets/Scenes/Main.unity` and press Play.

## Editor menu `Rubik`

- **Create Main Scene** — regenerates `Main.unity` (camera, light, cube, UI).
- **Solver Smoke Test** — scrambles, solves and verifies a cube in the background, logs to Console.
- **Apply Project Settings** — product name, windowed 1600x900, MSAA 4x.
- **Build Windows** — applies the settings and builds `RubikApp/Builds/Windows/RubikSolver.exe`.

## Scripts

- `CubeView` — 3D NxN cube; `Rubik.Core.Cube` is the source of truth, moves are animated then applied.
- `CubeAppController` — moves, scramble, solve (background thread), edit mode, solution player.
- `CubeEditor` — colour input state; validity check is `Rubik.Solver3x3.CubeValidator`.
- `CubeUI` — UI Toolkit panel built in code: net, palette, buttons, player.
- `OrbitCamera` — drag to orbit, wheel to zoom.
