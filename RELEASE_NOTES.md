# Rubik Solver 1.0.0

First public release.

## Download

- **Windows (64-bit):** `RubikSolver-1.0.0-win64.zip` below. Unzip anywhere and run `RubikSolver.exe`.
  The app is not code-signed, so Windows SmartScreen may warn on the first start: click **More info → Run anyway**.
- **Web:** https://gaveloshpom.github.io/RubikSolver/

## What it does

- Animated 3D cube: drag a sticker to turn its layer, drag the background to orbit, mouse wheel to zoom.
- **Enter my cube:** paint the stickers on the net or on the 3D cube (white centre up, green centre facing you).
  The app tells you if the cube is impossible (wrong color count, twisted corner, flipped edge, swapped pieces).
- **Solve:** two-phase search with a layer-by-layer fallback.
- **Solution player:** step forward/back, play/pause, jump to any move, speed from 0.5 to 12 moves per second;
  the next move is shown in large letters so you can follow along on a real cube.
- English and Ukrainian, UI size from 60% to 180%.

## Keyboard

U R F D L B, x y z: moves (Shift = counter-clockwise) · Space: scramble · Enter: solve · Backspace: reset ·
← →: step through the solution · P: play/pause · Home/End: start/end · +/-: speed
