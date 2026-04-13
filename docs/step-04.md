# Step 04 - Work/Break State Flow

This step adds a simple mode-based timer flow with short durations for testing.

## Modes

The app now uses a `mode` variable with these values:

- `idle`
- `work`
- `break`
- `paused`

## Durations

- Work duration: **80 seconds**
- Break duration: **20 seconds**

## Status label mapping

- `idle` -> `Status: Idle`
- `work` -> `Status: Working`
- `break` -> `Status: Break`
- `paused` -> `Status: Paused`

## Button behavior

### Start

- If mode is `idle`, Start begins a new work session (80 seconds).
- If mode is `paused`, Start resumes the previously paused mode (`work` or `break`).

### Pause

- Pause works while actively counting down in either `work` or `break` mode.
- The app remembers the previous active mode so Start can resume it.

### Reset

- Reset cancels any active countdown.
- Reset always returns to `idle` mode.
- Reset sets the timer display back to the work duration (80 seconds).

## Automatic transitions

- When `work` reaches `00:00`, the app automatically switches to `break` and starts the break countdown.
- When `break` reaches `00:00`, the app switches to `idle` and stops.

## Constraints kept

- No automatic looping after break ends.
- No notifications.
- No local storage.
- UI layout and colors remain unchanged.
