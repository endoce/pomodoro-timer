# Layout Fix - Vertical 01

The window height was increased so all UI elements are visible at startup after adding the statistics label.

## What changed

- Updated window geometry from `420x280` to `420x340`.

## Result

At startup, the full interface is visible without clipping:

- timer
- status label
- statistics label
- Start / Pause / Reset buttons (still on one horizontal row)

No timer/state/statistics behavior was changed.
