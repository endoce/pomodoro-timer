# Step 06 - Local Stats Persistence (stats.json)

This step keeps the same timer behavior and UI, but adds simple local file storage for statistics.

## Durations used for this step

- Work: **30 seconds**
- Break: **10 seconds**

## File format

Statistics are stored in a file named `stats.json` in the project directory.

Example:

```json
{
  "completed_sessions": 3,
  "focus_seconds": 90
}
```

Fields:

- `completed_sessions`: total number of finished work sessions
- `focus_seconds`: total focused work time in seconds

## Load behavior on startup

When the app starts:

1. It checks whether `stats.json` exists.
2. If the file exists and is valid JSON, it loads both fields.
3. If the file does not exist, both values start at `0`.

## Save behavior

Statistics still update only when a **work** session reaches `00:00`.

At that moment:

- `completed_sessions` increases by `1`
- `focus_seconds` adds `WORK_SECONDS` (`30`)
- The updated values are saved to `stats.json`

Break sessions are not counted.

## Error fallback behavior

If `stats.json` is invalid/corrupted, or cannot be read:

- The app falls back to safe defaults (`0`, `0`)
- The app continues running without crashing

If saving fails (for example, temporary file system issues):

- The app ignores the save error
- Timer behavior continues normally

## Reset behavior

Reset still affects timer state only:

- Stops active countdown
- Returns to idle mode
- Restores work duration on screen

Reset does **not** clear saved statistics.
