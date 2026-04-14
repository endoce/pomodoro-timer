# Step 14 - Safe window close handling

## What was added
- Connected the window close button (`WM_DELETE_WINDOW`) to a single `on_close` handler.
- Added a `shutdown_app` helper so close behavior is in one place.

## When confirmation appears
A confirmation dialog is shown if the app is currently in one of these states:
- `work`
- `break`
- `long_break`
- `paused`

If the app is `idle`, it closes immediately without showing confirmation.

## What happens on confirm
When the user confirms exit:
- Any pending timer callback is canceled safely.
- Statistics are saved one more time.
- The app window is destroyed cleanly.

## What happens on cancel
When the user cancels the confirmation dialog:
- The app stays open.
- Timer state and UI remain unchanged.
