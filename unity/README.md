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
- **Apply Project Settings** — product name, version, windowed 1600x900, MSAA 4x.
- **Build Windows** — builds `RubikApp/Builds/Windows/RubikSolver.exe` for local testing.
- **Build Release** — builds and zips `RubikApp/Builds/Release/RubikSolver-<version>-win64.zip` for GitHub Releases.
- **Build Web** — builds the WebGL site into `RubikApp/Builds/Web` (gzip with decompression fallback, `Responsive` template).

The version lives in `RubikBuild.Version` (`Assets/Editor/RubikBuild.cs`).

## Publishing

1. **Windows:** Rubik → Build Release, then create a GitHub release (tag `v<version>`), paste `RELEASE_NOTES.md`
   and attach the zip.
2. **Web:** Rubik → Build Web, then `powershell -ExecutionPolicy Bypass -File unity/publish-web.ps1 -Push`.
   It commits the build to the `gh-pages` branch (via a temporary worktree) and pushes it.
   In the GitHub repository settings, Pages must serve the `gh-pages` branch, root folder.

In the browser there are no threads, so the solver runs on the main thread (`CubeAppController.RunWork`).

## Scripts

- `CubeView` — 3D NxN cube; `Rubik.Core.Cube` is the source of truth, moves are animated then applied.
- `CubeAppController` — moves, scramble, solve (background thread), edit mode, solution player.
- `CubeEditor` — colour input state; validity check is `Rubik.Solver3x3.CubeValidator`.
- `CubeUI` — UI Toolkit panel built in code: net, palette, buttons, player.
- `OrbitCamera` — drag to orbit, wheel to zoom.
