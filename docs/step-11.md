# Step 11 - Long Break Mode

This step adds a configurable **long break** phase that is triggered after a configured number of completed work sessions.

## New config fields

Add these fields to `config.json`:

- `long_break_seconds` (default: `20`)
  - Duration (in seconds) for long breaks.

- `long_break_every` (default: `3`)
  - After every N completed work sessions, the next break becomes a long break.

## Safe loading behavior

The app now loads `long_break_seconds` and `long_break_every` with safe fallback defaults:

- Missing values use `DEFAULT_CONFIG`.
- Invalid or non-positive values fall back to defaults.
- Existing settings (`work_seconds`, `break_seconds`, `auto_start_break`, `auto_start_work`) keep the same safe-loading behavior.

## Long break trigger rule

When a work session reaches `00:00`:

1. `completed_sessions` increases by 1.
2. `focus_seconds` increases by `work_seconds`.
3. If `completed_sessions % long_break_every == 0`, the app enters `long_break`.
4. Otherwise, the app enters normal `break`.

## Phase transitions with long_break

- **Work → Break or Long Break**
  - Uses `break_seconds` for normal breaks.
  - Uses `long_break_seconds` for long breaks.
  - `auto_start_break` controls auto-start for both break types.

- **Break or Long Break → Work/Idle**
  - If `auto_start_work` is `true`, the next work session starts automatically.
  - If `auto_start_work` is `false`, app returns to idle and shows full work duration.

## Status label updates

Status text now supports all phases:

- Idle
- Working
- Break
- Long Break
- Paused

## Reminder behavior

Reminders still appear at phase boundaries, and long-break reminders are now distinct from normal-break reminders.

## Statistics behavior

Statistics logic is unchanged:

- Only completed **work** sessions update statistics.
- Completing `break` or `long_break` does not change statistics.
