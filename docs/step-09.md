# Step 09 - Clear Stats Button

This step adds a dedicated **Clear Stats** feature so users can reset saved statistics without resetting the timer.

## What Clear Stats does

- Adds a separate **Clear Stats** button in the main UI.
- When confirmed, it resets:
  - `completed_sessions` to `0`
  - `focus_seconds` to `0`
- Refreshes the statistics label immediately.
- Saves the cleared values to `stats.json`.

## Confirmation behavior

- Clicking **Clear Stats** opens a confirmation dialog with `messagebox.askyesno`.
- If the user selects **Yes**, stats are cleared and saved.
- If the user selects **No**, nothing changes.

## What it does not affect

- It does **not** change timer mode (`idle`, `work`, `break`, or `paused`).
- It does **not** change the remaining timer value.
- It does **not** change Start/Pause/Reset logic.
- It does **not** change reminder behavior.
- It does **not** change `config.json` behavior.
