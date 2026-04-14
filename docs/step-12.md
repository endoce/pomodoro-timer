# Step 12 - Cycle Progress Label

This step adds a visible cycle progress label so users can see how close they are to the next long break.

## What the cycle label means

The app now shows a label in the UI:

- `Cycle: X / N`

Where:

- `N` is `long_break_every` from `config.json`
- `X` is current progress in the work-session cycle

Example with `long_break_every = 3`:

- After 0 completed work sessions: `Cycle: 0 / 3`
- After 1 completed work session: `Cycle: 1 / 3`
- After 2 completed work sessions: `Cycle: 2 / 3`
- After 3 completed work sessions: `Cycle: 3 / 3` (long break trigger point)

## How X is calculated

`X` is calculated from saved statistics using:

1. `completed_sessions % long_break_every`
2. If the modulo result is `0` and at least one work session has been completed, show `X = long_break_every`

This keeps the label intuitive at long-break trigger points.

## When the cycle label updates

The cycle label updates in these places:

- On app startup
- After each completed work session
- After **Clear Stats**

## Notes

- Reset behavior is unchanged: Reset does not clear stats, so cycle progress is unchanged.
- Timer logic, reminder logic, and statistics logic are unchanged except for display updates.
- UI style remains the same; only a small vertical spacing/height adjustment was made so all content stays visible.
