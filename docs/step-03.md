# Step 03

## Goal

Add a visible status label to the timer UI.

## Scope

This step includes:
- one Tkinter window in a single file (`main.py`)
- timer label with Start / Pause / Reset controls from Step 02
- a status label showing current timer state
- status is `Idle` when the app starts
- status is `Running` while countdown is active
- status is `Paused` when Pause is clicked
- status returns to `Idle` on Reset
- status returns to `Idle` when countdown reaches `00:00`

This step does not include:
- break mode
- notifications
- file storage

## Completion Criteria

The step is complete when:
1. `python main.py` starts successfully
2. the app initially shows `Status: Idle`
3. clicking **Start** changes status to `Status: Running`
4. clicking **Pause** changes status to `Status: Paused`
5. clicking **Reset** changes status to `Status: Idle` and restores `25:00`
6. when the timer reaches `00:00`, status becomes `Status: Idle`
7. Start / Pause / Reset timer behavior still works as in Step 02
