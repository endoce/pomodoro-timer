# Step 07 - Load Timer Settings from config.json

This step adds simple configuration loading so timer settings are no longer hardcoded in code.

## Config structure

The app reads a `config.json` file from the project directory.

Expected format:

```json
{
  "window_title": "Pomodoro Timer",
  "work_seconds": 30,
  "break_seconds": 10
}
```

Fields:

- `window_title`: text used for the app window title
- `work_seconds`: work session length in seconds
- `break_seconds`: break session length in seconds

## Default fallback behavior

The app has built-in defaults that match the values above.

On startup:

1. If `config.json` exists and is valid, the app uses values from the file.
2. If `config.json` does not exist, the app uses defaults.
3. If `config.json` is invalid or unreadable, the app falls back to defaults without crashing.

## How config values are used

- `window_title` sets the window title.
- `work_seconds` is used for:
  - initial timer value
  - starting work sessions
  - reset behavior (Reset returns to configured work duration)
  - focus time accumulation when a work session completes
- `break_seconds` is used when switching from work mode to break mode.

## Scope

- No settings UI was added.
- Config is read-only at runtime (no auto-save of config).
- Statistics behavior and UI style remain unchanged.
