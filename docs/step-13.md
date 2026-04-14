# Step 13 - Skip control and vertical layout fix

## What changed
- Added a new **Skip** button on the utility row beside **Clear Stats**.
- Adjusted vertical spacing and increased window height so the full interface is visible at startup.
- Kept the existing color palette and overall visual style.

## Skip behavior by phase
- **Idle**
  - Skip does nothing.

- **Work (running or paused)**
  - Skip cancels the current work session.
  - App returns to **Idle**.
  - Timer is reset to the configured work duration.

- **Break (running or paused)**
  - Skip immediately switches to **Work**.
  - Work countdown starts right away.

- **Long break (running or paused)**
  - Skip immediately switches to **Work**.
  - Work countdown starts right away.

## What Skip does not count
- Skip does **not** count as normal phase completion.
- Skip does **not** increment `completed_sessions`.
- Skip does **not** add to `focus_seconds`.
- Skip does **not** trigger cycle progress updates.
- Skip does **not** change reminder behavior for normal timer completion.

## Layout fix summary
- Increased the window from `420x360` to `420x390`.
- Tightened label/button vertical spacing slightly.
- Moved utility controls to a dedicated row to keep all controls visible with comfortable bottom breathing room:
  - Clear Stats
  - Skip
