# Pomodoro Timer

A simple desktop pomodoro timer built with Python and Tkinter.

## Project Goal

This project is a beginner-friendly practice project.

The target is to build a local timer app step by step, starting from the most basic version:
- show a window
- display 25:00
- click **Start** to count down every second

## Tech Stack

- Python
- Tkinter
- GitHub repository as the source of truth
- Codex web for code generation and editing

## Current Step

Step 02: single-file timer with Start / Pause / Reset

## Current Implementation (Step 02)

- one Tkinter window
- one large timer label
- **Start** button to start or resume
- **Pause** button using `after_cancel`
- **Reset** button restoring the timer to `25:00`
- duplicate Start clicks do not create parallel timers
- countdown from 25:00 to 00:00

Not included in this step:
- break mode
- notifications
- local file storage

## Local Run

```bash
python main.py
```
