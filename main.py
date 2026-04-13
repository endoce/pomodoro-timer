import tkinter as tk
from tkinter import messagebox
import json
from pathlib import Path

BG_COLOR = "#F3FFF8"
CARD_COLOR = "#FFFFFF"
TIMER_COLOR = "#FF6F61"
STATUS_IDLE_COLOR = "#5C6F7B"
STATUS_RUNNING_COLOR = "#2E9E5B"
STATUS_PAUSED_COLOR = "#F39C3D"
STATS_FILE = Path("stats.json")
CONFIG_FILE = Path("config.json")
DEFAULT_CONFIG = {
    "window_title": "Pomodoro Timer",
    "work_seconds": 30,
    "break_seconds": 10,
}


def load_config():
    # Start with safe defaults.
    config_data = DEFAULT_CONFIG.copy()

    # If config.json does not exist, keep defaults.
    if not CONFIG_FILE.exists():
        return config_data

    try:
        with CONFIG_FILE.open("r", encoding="utf-8") as file:
            loaded_data = json.load(file)

        # Merge known keys from the file.
        # Any missing key falls back to DEFAULT_CONFIG.
        config_data["window_title"] = str(
            loaded_data.get("window_title", DEFAULT_CONFIG["window_title"])
        )
        config_data["work_seconds"] = int(
            loaded_data.get("work_seconds", DEFAULT_CONFIG["work_seconds"])
        )
        config_data["break_seconds"] = int(
            loaded_data.get("break_seconds", DEFAULT_CONFIG["break_seconds"])
        )
    except (json.JSONDecodeError, OSError, TypeError, ValueError):
        # Invalid or unreadable config should not crash the app.
        return DEFAULT_CONFIG.copy()

    return config_data


class PomodoroApp:
    def __init__(self, root):
        # Keep a reference to the main Tkinter window.
        self.root = root
        self.config_data = load_config()
        self.window_title = self.config_data["window_title"]
        self.work_seconds = self.config_data["work_seconds"]
        self.break_seconds = self.config_data["break_seconds"]
        self.root.title(self.window_title)
        # Increased height so timer, labels, and buttons are visible at startup.
        self.root.geometry("420x340")
        self.root.configure(bg=BG_COLOR)

        # mode tracks the high-level app state.
        # Allowed values: "idle", "work", "break", "paused"
        self.mode = "idle"

        # paused_mode remembers which mode was active before pausing.
        # This lets Start resume the correct mode.
        self.paused_mode = None

        # remaining_seconds stores how much time is left in the current mode.
        self.remaining_seconds = self.work_seconds

        # timer_job stores the scheduled `after` callback id.
        # It is None when no countdown callback is currently scheduled.
        self.timer_job = None

        # Basic in-memory statistics.
        # completed_sessions counts finished work sessions.
        # focus_seconds tracks total completed work time in seconds.
        self.completed_sessions = 0
        self.focus_seconds = 0
        self.load_stats()

        # Main card keeps content grouped and centered with clean spacing.
        self.main_frame = tk.Frame(root, bg=CARD_COLOR, padx=24, pady=24)
        self.main_frame.pack(padx=16, pady=16, fill="both", expand=True)

        # Large playful timer text in a warm accent color.
        self.time_label = tk.Label(
            self.main_frame,
            text=self.format_time(self.remaining_seconds),
            font=("Arial Rounded MT Bold", 44),
            fg=TIMER_COLOR,
            bg=CARD_COLOR
        )
        self.time_label.pack(pady=(0, 14))

        # Status label is styled to stand out clearly under the timer.
        self.status_label = tk.Label(
            self.main_frame,
            text="Status: Idle",
            font=("Arial", 13, "bold"),
            fg=STATUS_IDLE_COLOR,
            bg="#EAF4FF",
            padx=14,
            pady=6
        )
        self.status_label.pack(pady=(0, 16))

        # Stats label shows completed work sessions and total focus time.
        self.stats_label = tk.Label(
            self.main_frame,
            text=f"Completed: {self.completed_sessions} | Focus: {self.focus_seconds}s",
            font=("Arial", 11, "bold"),
            fg=STATUS_IDLE_COLOR,
            bg="#EAF4FF",
            padx=14,
            pady=4
        )
        self.stats_label.pack(pady=(0, 16))

        # Use one frame so the three control buttons stay on one row.
        self.button_frame = tk.Frame(self.main_frame, bg=CARD_COLOR)
        self.button_frame.pack()

        # Start begins or resumes the countdown.
        self.start_button = tk.Button(
            self.button_frame,
            text="Start",
            width=10,
            bg="#4CAF6D",
            fg="white",
            activebackground="#43A861",
            activeforeground="white",
            font=("Arial", 11, "bold"),
            relief="raised",
            bd=2,
            command=self.start_timer
        )
        self.start_button.grid(row=0, column=0, padx=4)

        # Pause stops the scheduled countdown callback with after_cancel.
        self.pause_button = tk.Button(
            self.button_frame,
            text="Pause",
            width=10,
            bg="#F4A340",
            fg="white",
            activebackground="#E39230",
            activeforeground="white",
            font=("Arial", 11, "bold"),
            relief="raised",
            bd=2,
            command=self.pause_timer
        )
        self.pause_button.grid(row=0, column=1, padx=4)

        # Reset stops any running timer and restores 25:00.
        self.reset_button = tk.Button(
            self.button_frame,
            text="Reset",
            width=10,
            bg="#7CC8F8",
            fg="white",
            activebackground="#6EBDEB",
            activeforeground="white",
            font=("Arial", 11, "bold"),
            relief="raised",
            bd=2,
            command=self.reset_timer
        )
        self.reset_button.grid(row=0, column=2, padx=4)

    def format_time(self, seconds):
        # Convert total seconds into MM:SS format.
        minutes = seconds // 60
        remaining_seconds = seconds % 60
        return f"{minutes:02d}:{remaining_seconds:02d}"

    def update_display(self):
        # Refresh the label text using the latest remaining_seconds value.
        self.time_label.config(text=self.format_time(self.remaining_seconds))

    def update_stats_display(self):
        # Keep stats text updates in one place.
        self.stats_label.config(
            text=f"Completed: {self.completed_sessions} | Focus: {self.focus_seconds}s"
        )

    def load_stats(self):
        # Try reading stats from stats.json.
        # If anything goes wrong, keep safe zero defaults.
        if not STATS_FILE.exists():
            return

        try:
            with STATS_FILE.open("r", encoding="utf-8") as file:
                stats_data = json.load(file)

            self.completed_sessions = int(stats_data.get("completed_sessions", 0))
            self.focus_seconds = int(stats_data.get("focus_seconds", 0))
        except (json.JSONDecodeError, OSError, TypeError, ValueError):
            self.completed_sessions = 0
            self.focus_seconds = 0

    def save_stats(self):
        # Save current stats to stats.json.
        # Ignore file errors so the timer never crashes.
        stats_data = {
            "completed_sessions": self.completed_sessions,
            "focus_seconds": self.focus_seconds,
        }
        try:
            with STATS_FILE.open("w", encoding="utf-8") as file:
                json.dump(stats_data, file)
        except OSError:
            pass

    def set_status(self):
        # Keep status text and color updates in one place for clarity.
        status_texts = {
            "idle": "Idle",
            "work": "Working",
            "break": "Break",
            "paused": "Paused",
        }
        status_colors = {
            "idle": STATUS_IDLE_COLOR,
            "work": STATUS_RUNNING_COLOR,
            "break": STATUS_RUNNING_COLOR,
            "paused": STATUS_PAUSED_COLOR,
        }
        self.status_label.config(
            text=f"Status: {status_texts.get(self.mode, 'Idle')}",
            fg=status_colors.get(self.mode, STATUS_IDLE_COLOR)
        )

    def start_timer(self):
        # Start has two behaviors:
        # 1) idle -> begin work mode
        # 2) paused -> resume whichever mode was paused
        if self.timer_job is not None:
            return

        if self.mode == "idle":
            self.mode = "work"
            self.remaining_seconds = self.work_seconds
        elif self.mode == "paused" and self.paused_mode in ("work", "break"):
            self.mode = self.paused_mode

        if self.mode in ("work", "break") and self.remaining_seconds > 0:
            self.set_status()
            self.countdown()

    def pause_timer(self):
        # Pause works during both work and break countdowns.
        if self.timer_job is not None and self.mode in ("work", "break"):
            self.root.after_cancel(self.timer_job)
            self.timer_job = None
            self.paused_mode = self.mode
            self.mode = "paused"
            self.set_status()

    def reset_timer(self):
        # Reset should stop any running timer and return to idle.
        if self.timer_job is not None:
            self.root.after_cancel(self.timer_job)
            self.timer_job = None

        self.mode = "idle"
        self.paused_mode = None
        self.remaining_seconds = self.work_seconds
        self.update_display()
        self.set_status()

    def countdown(self):
        # Countdown behavior depends on the active mode.
        if self.remaining_seconds > 0:
            self.remaining_seconds -= 1
            self.update_display()
            self.timer_job = self.root.after(1000, self.countdown)
        else:
            # Reaching 00:00 changes mode:
            # work -> break (auto-start break)
            # break -> idle (stop)
            self.timer_job = None
            if self.mode == "work":
                # Count only completed work sessions.
                self.completed_sessions += 1
                self.focus_seconds += self.work_seconds
                self.save_stats()
                self.update_stats_display()

                # Move into break mode and start break countdown immediately.
                self.mode = "break"
                self.remaining_seconds = self.break_seconds
                self.set_status()
                self.update_display()

                # Show one reminder right when work finishes.
                # This branch runs only once per completed work session.
                messagebox.showinfo(
                    "Work Session Complete",
                    "Great job! Your work session is complete. Break time has started."
                )

                self.countdown()
            elif self.mode == "break":
                # Break is done, so return to idle with a full work timer ready.
                self.mode = "idle"
                self.paused_mode = None
                self.remaining_seconds = self.work_seconds
                self.update_display()
                self.set_status()

                # Show one reminder right when the break finishes.
                # Pause and Reset do not call this branch, so they never show reminders.
                messagebox.showinfo(
                    "Break Complete",
                    "Your break is complete. You are ready to start the next work session."
                )


if __name__ == "__main__":
    # Create app window and start Tkinter event loop.
    root = tk.Tk()
    app = PomodoroApp(root)
    root.mainloop()
