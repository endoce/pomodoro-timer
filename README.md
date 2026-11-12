# Pomodoro Timer

## Pomodoro Timer 2.0 — Portable WPF work in progress

The 2.0 direction is .NET 10 LTS / C# / WPF / Windows App SDK on Windows 11 25H2 x64 (26200.x), with **no installer**. The planned single application download is `PomodoroTimer-Portable-Windows-x64.zip`: extract to a normal-user writable directory, run `PomodoroTimer.exe`, and keep its local `Data/` and `Logs/` when moving the whole folder.

The implementation and Portable-only CI changes are awaiting review; a successful Windows build, real release ZIP and desktop acceptance have **not** been established. See [WPF spike](spikes/wpf-risk-validation/README.md) and [current validation status](spikes/wpf-risk-validation/docs/STATUS.md). The Python/Tkinter description below is the historical implementation, not the 2.0 release.

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
