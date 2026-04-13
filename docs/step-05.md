# Step 05 - In-Memory Statistics

This step adds simple statistics that exist only while the app is running.

## Durations used for this step

- Work: **30 seconds**
- Break: **10 seconds**

## New statistics variables

Two integer variables are stored in memory:

- `completed_sessions`: number of finished work sessions
- `focus_seconds`: total focus time from finished work sessions

Both values start at `0` when the app starts.

## When statistics change

Statistics update only when a **work** session reaches `00:00`.

At that moment:

- `completed_sessions` is increased by `1`
- `focus_seconds` adds `WORK_SECONDS` (30)

Break sessions are **not** counted.

## Statistics label

A label is shown in the UI with this format:

- `Completed: X | Focus: Ys`

Example:

- `Completed: 2 | Focus: 60s`

## Reset behavior

Reset still resets timer state only:

- Returns timer mode to idle
- Restores timer display to work duration

Reset does **not** clear statistics.

## Scope and constraints

- No file/database storage (memory only)
- No notifications
- Existing style/layout kept close to current UI
- Single-file app structure preserved
