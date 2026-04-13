# Step 08: Reminder Popups

This step adds simple Tkinter reminder popups using `messagebox.showinfo`.

## When reminders are shown

Reminders are shown only when a timer phase naturally reaches `00:00`:

1. **Work session ends**
   - Statistics are updated first (completed sessions and focus seconds).
   - The app switches to **break mode**.
   - The break timer starts.
   - A popup appears to tell the user work is complete and break time has started.

2. **Break session ends**
   - The app switches to **idle mode**.
   - The timer is reset to the configured work duration.
   - A popup appears to tell the user the break is complete.

## Popup messages

- **Work end popup**
  - Title: `Work Session Complete`
  - Message: `Great job! Your work session is complete. Break time has started.`

- **Break end popup**
  - Title: `Break Complete`
  - Message: `Your break is complete. You are ready to start the next work session.`

## What does not trigger reminders

No popup is shown for:

- Pressing **Pause**
- Pressing **Reset**
- Starting or resuming a timer before it reaches `00:00`

This keeps reminders limited to true phase completion events, so each completed phase shows one reminder.
