# Step 02

## Goal

Extend the basic timer to support pause and reset controls.

## Scope

This step includes:
- one Tkinter window
- one large timer label
- one **Start** button
- one **Pause** button
- one **Reset** button
- countdown from 25:00 to 00:00
- pause implemented via `after_cancel`
- reset restores `25:00`
- prevent duplicate timer start (no parallel timers)

This step does not include:
- break mode
- notifications
- local file storage
- exe packaging

## Completion Criteria

The step is complete when:
1. `python main.py` starts successfully
2. the window shows `25:00`
3. clicking **Start** runs countdown once per second
4. clicking **Pause** freezes the countdown
5. clicking **Start** again resumes countdown
6. clicking **Reset** restores `25:00`
7. repeated clicks on **Start** do not speed up countdown

## Notes

Keep the implementation in a single file (`main.py`).
Testing is done locally.
