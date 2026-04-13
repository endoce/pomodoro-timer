# Step 10 - Configurable Automatic Phase Transitions

This step adds configurable automatic transitions between **work** and **break** phases using `config.json`.

## New config fields

Add these fields to `config.json`:

- `auto_start_break` (default: `true`)
  - When `true`, break starts automatically after work completes.
  - When `false`, timer switches to break phase and waits for **Start**.

- `auto_start_work` (default: `false`)
  - When `true`, work starts automatically after break completes.
  - When `false`, timer returns to idle with full work duration and waits for **Start**.

## Safe loading behavior

The app now reads both fields with safe fallbacks:

- Missing fields use defaults from `DEFAULT_CONFIG`.
- Invalid values fall back safely without crashing.
- Boolean text values like `"true"` / `"false"` are accepted.

## Transition behavior

### When a work session ends

1. Work statistics are updated (`completed_sessions`, `focus_seconds`).
2. Timer switches to break phase and loads configured break duration.
3. If `auto_start_break` is `true`, break countdown begins immediately.
4. If `auto_start_break` is `false`, break phase waits for **Start**.

### When a break session ends

1. Statistics are not changed.
2. If `auto_start_work` is `true`, app switches to work phase and starts immediately.
3. If `auto_start_work` is `false`, app returns to idle and shows full work duration.

## Start button behavior

The **Start** button now behaves correctly in all relevant states:

- **Idle →** starts a work session.
- **Paused →** resumes the paused phase (work or break).
- **Break waiting to start →** starts break countdown.
- **Work waiting to start (internal edge case) →** starts work countdown.

## Notes

- No new UI controls were added.
- UI layout and style remain unchanged.
- Long-break logic is still not included.
- Reminder messages stay aligned with real transition outcomes.
