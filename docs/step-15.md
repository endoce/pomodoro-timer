# Step 15: Settings dialog

This step adds a **Settings** button in the main window so timer values can be changed from the UI.

## Changeable settings

The Settings dialog now lets you edit:

- `work_seconds`
- `break_seconds`
- `long_break_seconds`
- `long_break_every`
- `auto_start_break`
- `auto_start_work`

## Validation rules

When **Save** is clicked:

- Numeric fields (`work_seconds`, `break_seconds`, `long_break_seconds`, `long_break_every`) must be positive integers.
- If any numeric field is invalid:
  - an error dialog is shown,
  - nothing is saved,
  - and the Settings dialog stays open.

If all values are valid, settings are saved in memory and written to `config.json`.

## When the main timer display updates immediately

After a successful save:

- If the app is **idle**, the main timer display immediately refreshes to the new `work_seconds` value.
- If the app is **running** or **paused**, the current remaining countdown time is left unchanged.
