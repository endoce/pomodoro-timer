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
    "auto_start_break": True,
    "auto_start_work": False,
    "long_break_seconds": 20,
    "long_break_every": 3,
}


def parse_bool(value, default):
    # Accept true/false values safely from config.json.
    if isinstance(value, bool):
        return value

    # Also accept common text values so config edits are beginner-friendly.
    if isinstance(value, str):
        normalized = value.strip().lower()
        if normalized in ("true", "1", "yes", "on"):
            return True
        if normalized in ("false", "0", "no", "off"):
            return False

    # Any unexpected value falls back to the provided default.
    return default


def parse_positive_int(value, default):
    # Read numeric config values safely and keep a positive fallback.
    try:
        parsed_value = int(value)
        if parsed_value > 0:
            return parsed_value
    except (TypeError, ValueError):
        pass

    return default


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
        config_data["work_seconds"] = parse_positive_int(
            loaded_data.get("work_seconds", DEFAULT_CONFIG["work_seconds"]),
            DEFAULT_CONFIG["work_seconds"],
        )
        config_data["break_seconds"] = parse_positive_int(
            loaded_data.get("break_seconds", DEFAULT_CONFIG["break_seconds"]),
            DEFAULT_CONFIG["break_seconds"],
        )
        config_data["auto_start_break"] = parse_bool(
            loaded_data.get("auto_start_break", DEFAULT_CONFIG["auto_start_break"]),
            DEFAULT_CONFIG["auto_start_break"],
        )
        config_data["auto_start_work"] = parse_bool(
            loaded_data.get("auto_start_work", DEFAULT_CONFIG["auto_start_work"]),
            DEFAULT_CONFIG["auto_start_work"],
        )
        config_data["long_break_seconds"] = parse_positive_int(
            loaded_data.get("long_break_seconds", DEFAULT_CONFIG["long_break_seconds"]),
            DEFAULT_CONFIG["long_break_seconds"],
        )
        config_data["long_break_every"] = parse_positive_int(
            loaded_data.get("long_break_every", DEFAULT_CONFIG["long_break_every"]),
            DEFAULT_CONFIG["long_break_every"],
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
        self.auto_start_break = self.config_data["auto_start_break"]
        self.auto_start_work = self.config_data["auto_start_work"]
        self.long_break_seconds = self.config_data["long_break_seconds"]
        self.long_break_every = self.config_data["long_break_every"]
        self.root.title(self.window_title)
        # Increased height so all labels and control buttons are visible at startup.
        self.root.geometry("420x430")
        self.root.configure(bg=BG_COLOR)
        # Use one close handler for the window X button.
        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

        # mode tracks the high-level app state.
        # Allowed values: "idle", "work", "break", "long_break", "paused"
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
        self.main_frame = tk.Frame(root, bg=CARD_COLOR, padx=24, pady=20)
        self.main_frame.pack(padx=16, pady=16, fill="both", expand=True)

        # Large playful timer text in a warm accent color.
        self.time_label = tk.Label(
            self.main_frame,
            text=self.format_time(self.remaining_seconds),
            font=("Arial Rounded MT Bold", 44),
            fg=TIMER_COLOR,
            bg=CARD_COLOR
        )
        self.time_label.pack(pady=(0, 12))

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
        self.status_label.pack(pady=(0, 12))

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
        self.stats_label.pack(pady=(0, 8))

        # Cycle label shows progress toward the next long break.
        self.cycle_label = tk.Label(
            self.main_frame,
            text="",
            font=("Arial", 11, "bold"),
            fg=STATUS_IDLE_COLOR,
            bg="#EAF4FF",
            padx=14,
            pady=4
        )
        self.cycle_label.pack(pady=(0, 12))
        self.update_cycle_display()

        # Use one frame so the three control buttons stay on one row.
        self.button_frame = tk.Frame(self.main_frame, bg=CARD_COLOR)
        self.button_frame.pack(pady=(0, 8))

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

        # Keep utility buttons on their own row to avoid crowding primary controls.
        self.utility_button_frame = tk.Frame(self.main_frame, bg=CARD_COLOR)
        self.utility_button_frame.pack(pady=(8, 0))

        self.clear_stats_button = tk.Button(
            self.utility_button_frame,
            text="Clear Stats",
            width=12,
            bg="#7CC8F8",
            fg="white",
            activebackground="#6EBDEB",
            activeforeground="white",
            font=("Arial", 10, "bold"),
            relief="raised",
            bd=2,
            command=self.clear_stats
        )
        self.clear_stats_button.grid(row=0, column=0, padx=4)

        # Skip lets users move forward without counting a normal completion.
        self.skip_button = tk.Button(
            self.utility_button_frame,
            text="Skip",
            width=12,
            bg="#F4A340",
            fg="white",
            activebackground="#E39230",
            activeforeground="white",
            font=("Arial", 10, "bold"),
            relief="raised",
            bd=2,
            command=self.skip_phase
        )
        self.skip_button.grid(row=0, column=1, padx=4)

        # Settings gets its own row so controls stay readable in the default window.
        self.settings_button = tk.Button(
            self.main_frame,
            text="Settings",
            width=12,
            bg="#7CC8F8",
            fg="white",
            activebackground="#6EBDEB",
            activeforeground="white",
            font=("Arial", 10, "bold"),
            relief="raised",
            bd=2,
            command=self.open_settings_dialog
        )
        self.settings_button.pack(pady=(8, 0))

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

    def update_cycle_display(self):
        # Show cycle progress in the format: Cycle: X / N.
        cycle_total = self.long_break_every

        # X uses completed work sessions and wraps after a long-break trigger.
        cycle_progress = self.completed_sessions % cycle_total
        if cycle_progress == 0 and self.completed_sessions > 0:
            cycle_progress = cycle_total

        self.cycle_label.config(text=f"Cycle: {cycle_progress} / {cycle_total}")

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

    def save_config(self):
        # Save current configuration to config.json.
        # Keep this in one helper so future config saves are easy to reuse.
        config_data = {
            "window_title": self.window_title,
            "work_seconds": self.work_seconds,
            "break_seconds": self.break_seconds,
            "auto_start_break": self.auto_start_break,
            "auto_start_work": self.auto_start_work,
            "long_break_seconds": self.long_break_seconds,
            "long_break_every": self.long_break_every,
        }
        with CONFIG_FILE.open("w", encoding="utf-8") as file:
            json.dump(config_data, file, indent=2)

    def open_settings_dialog(self):
        # Build a simple top-level window for editing timer settings.
        dialog = tk.Toplevel(self.root)
        dialog.title("Settings")
        dialog.configure(bg=CARD_COLOR)
        dialog.resizable(False, False)
        dialog.transient(self.root)
        dialog.grab_set()

        # Keep all field variables together so Save can validate and read them.
        numeric_fields = {}
        bool_fields = {}

        # Helper to render one numeric entry row.
        def add_numeric_row(row_index, label_text, key_name, current_value):
            tk.Label(
                dialog,
                text=label_text,
                font=("Arial", 10),
                bg=CARD_COLOR
            ).grid(row=row_index, column=0, sticky="w", padx=12, pady=6)
            entry_var = tk.StringVar(value=str(current_value))
            tk.Entry(dialog, textvariable=entry_var, width=18).grid(
                row=row_index, column=1, sticky="ew", padx=12, pady=6
            )
            numeric_fields[key_name] = entry_var

        # Helper to render one boolean checkbox row.
        def add_bool_row(row_index, label_text, key_name, current_value):
            bool_var = tk.BooleanVar(value=current_value)
            tk.Checkbutton(
                dialog,
                text=label_text,
                variable=bool_var,
                bg=CARD_COLOR,
                activebackground=CARD_COLOR
            ).grid(row=row_index, column=0, columnspan=2, sticky="w", padx=12, pady=6)
            bool_fields[key_name] = bool_var

        add_numeric_row(0, "Work seconds", "work_seconds", self.work_seconds)
        add_numeric_row(1, "Break seconds", "break_seconds", self.break_seconds)
        add_numeric_row(2, "Long break seconds", "long_break_seconds", self.long_break_seconds)
        add_numeric_row(3, "Long break every", "long_break_every", self.long_break_every)
        add_bool_row(4, "Auto-start break", "auto_start_break", self.auto_start_break)
        add_bool_row(5, "Auto-start work", "auto_start_work", self.auto_start_work)

        def save_settings():
            # Validate numeric values as positive integers before saving.
            parsed_values = {}
            for key_name, entry_var in numeric_fields.items():
                raw_value = entry_var.get().strip()
                try:
                    parsed_value = int(raw_value)
                except ValueError:
                    messagebox.showerror(
                        "Invalid Settings",
                        f"{key_name} must be a positive integer."
                    )
                    return

                if parsed_value <= 0:
                    messagebox.showerror(
                        "Invalid Settings",
                        f"{key_name} must be a positive integer."
                    )
                    return
                parsed_values[key_name] = parsed_value

            # Apply validated values to in-memory config.
            self.work_seconds = parsed_values["work_seconds"]
            self.break_seconds = parsed_values["break_seconds"]
            self.long_break_seconds = parsed_values["long_break_seconds"]
            self.long_break_every = parsed_values["long_break_every"]
            self.auto_start_break = bool_fields["auto_start_break"].get()
            self.auto_start_work = bool_fields["auto_start_work"].get()

            # Keep shared config_data in sync with the latest values.
            self.config_data["work_seconds"] = self.work_seconds
            self.config_data["break_seconds"] = self.break_seconds
            self.config_data["long_break_seconds"] = self.long_break_seconds
            self.config_data["long_break_every"] = self.long_break_every
            self.config_data["auto_start_break"] = self.auto_start_break
            self.config_data["auto_start_work"] = self.auto_start_work

            # Save config to disk; keep dialog open if writing fails.
            try:
                self.save_config()
            except OSError:
                messagebox.showerror(
                    "Save Failed",
                    "Could not write config.json. Please try again."
                )
                return

            # Refresh idle display to the new work duration only when idle.
            if self.mode == "idle":
                self.remaining_seconds = self.work_seconds
                self.update_display()

            # Cycle text depends on long_break_every, so refresh it after save.
            self.update_cycle_display()
            dialog.destroy()

        save_button = tk.Button(
            dialog,
            text="Save",
            width=12,
            bg="#4CAF6D",
            fg="white",
            activebackground="#43A861",
            activeforeground="white",
            font=("Arial", 10, "bold"),
            relief="raised",
            bd=2,
            command=save_settings
        )
        save_button.grid(row=6, column=0, columnspan=2, pady=(8, 12))

    def set_status(self):
        # Keep status text and color updates in one place for clarity.
        status_texts = {
            "idle": "Idle",
            "work": "Working",
            "break": "Break",
            "long_break": "Long Break",
            "paused": "Paused",
        }
        status_colors = {
            "idle": STATUS_IDLE_COLOR,
            "work": STATUS_RUNNING_COLOR,
            "break": STATUS_RUNNING_COLOR,
            "long_break": STATUS_RUNNING_COLOR,
            "paused": STATUS_PAUSED_COLOR,
        }
        self.status_label.config(
            text=f"Status: {status_texts.get(self.mode, 'Idle')}",
            fg=status_colors.get(self.mode, STATUS_IDLE_COLOR)
        )

    def start_timer(self):
        # Start supports these flows:
        # 1) idle -> begin work mode
        # 2) paused -> resume whichever mode was paused
        # 3) waiting work/break/long break phase -> begin that phase
        if self.timer_job is not None:
            return

        if self.mode == "idle":
            self.mode = "work"
            self.remaining_seconds = self.work_seconds
        elif self.mode == "paused" and self.paused_mode in ("work", "break", "long_break"):
            self.mode = self.paused_mode

        if self.mode in ("work", "break", "long_break") and self.remaining_seconds > 0:
            self.set_status()
            self.countdown()

    def pause_timer(self):
        # Pause works during both work and break countdowns.
        if self.timer_job is not None and self.mode in ("work", "break", "long_break"):
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

    def clear_stats(self):
        # Ask for confirmation so stats are not cleared by mistake.
        should_clear = messagebox.askyesno(
            "Clear Statistics",
            "Are you sure you want to clear completed sessions and focus time?"
        )

        # If the user cancels, keep everything exactly as it is.
        if not should_clear:
            return

        # Clear only statistics values.
        # Timer mode and remaining time are intentionally untouched.
        self.completed_sessions = 0
        self.focus_seconds = 0
        self.update_stats_display()
        self.update_cycle_display()
        self.save_stats()

    def skip_phase(self):
        # Skip does nothing when already idle.
        if self.mode == "idle":
            return

        # If paused, use the remembered phase to decide skip behavior.
        phase_to_skip = self.paused_mode if self.mode == "paused" else self.mode
        if phase_to_skip not in ("work", "break", "long_break"):
            return

        # Stop any active countdown callback before switching phases.
        if self.timer_job is not None:
            self.root.after_cancel(self.timer_job)
            self.timer_job = None

        if phase_to_skip == "work":
            # Skipping work cancels the session with no stat or cycle changes.
            self.mode = "idle"
            self.paused_mode = None
            self.remaining_seconds = self.work_seconds
            self.update_display()
            self.set_status()
        else:
            # Skipping break/long break starts the next work session immediately.
            self.mode = "work"
            self.paused_mode = None
            self.remaining_seconds = self.work_seconds
            self.set_status()
            self.update_display()
            self.countdown()

    def on_close(self):
        # Idle can close immediately because no session is in progress.
        if self.mode == "idle":
            self.shutdown_app()
            return

        # Active or paused phases ask for confirmation before closing.
        # This protects users from accidentally closing mid-session.
        should_exit = messagebox.askyesno(
            "Exit Pomodoro Timer",
            "A timer session is active or paused. Are you sure you want to exit?"
        )
        if not should_exit:
            # Cancel keeps the app running exactly as it is.
            return

        self.shutdown_app()

    def shutdown_app(self):
        # Safely stop scheduled callbacks before destroying the window.
        if self.timer_job is not None:
            self.root.after_cancel(self.timer_job)
            self.timer_job = None

        # Save one final copy of statistics before exiting.
        self.save_stats()
        self.root.destroy()

    def countdown(self):
        # Countdown behavior depends on the active mode.
        if self.remaining_seconds > 0:
            self.remaining_seconds -= 1
            self.update_display()
            self.timer_job = self.root.after(1000, self.countdown)
        else:
            # Reaching 00:00 changes mode based on current phase and config.
            self.timer_job = None
            if self.mode == "work":
                # Count only completed work sessions.
                self.completed_sessions += 1
                self.focus_seconds += self.work_seconds
                self.save_stats()
                self.update_stats_display()
                self.update_cycle_display()

                # Every N completed work sessions, use a long break.
                use_long_break = self.completed_sessions % self.long_break_every == 0
                next_mode = "long_break" if use_long_break else "break"
                next_seconds = self.long_break_seconds if use_long_break else self.break_seconds

                # Move into the chosen break mode and prepare full duration.
                self.mode = next_mode
                self.paused_mode = None
                self.remaining_seconds = next_seconds
                self.set_status()
                self.update_display()

                # Reminder text matches break type and auto-start behavior.
                if use_long_break:
                    waiting_text = "Great job! Your work session is complete. Press Start when you are ready for your long break."
                    started_text = "Great job! Your work session is complete. Long break time has started."
                else:
                    waiting_text = "Great job! Your work session is complete. Press Start when you are ready for your break."
                    started_text = "Great job! Your work session is complete. Break time has started."

                if self.auto_start_break:
                    messagebox.showinfo("Work Session Complete", started_text)
                    self.countdown()
                else:
                    messagebox.showinfo("Work Session Complete", waiting_text)
            elif self.mode in ("break", "long_break"):
                # Break and long break completion never change statistics.
                was_long_break = self.mode == "long_break"

                if self.auto_start_work:
                    self.mode = "work"
                    self.paused_mode = None
                    self.remaining_seconds = self.work_seconds
                    self.set_status()
                    self.update_display()
                    if was_long_break:
                        messagebox.showinfo(
                            "Long Break Complete",
                            "Your long break is complete. The next work session has started."
                        )
                    else:
                        messagebox.showinfo(
                            "Break Complete",
                            "Your break is complete. The next work session has started."
                        )
                    self.countdown()
                else:
                    # Return to idle with a fresh work timer waiting for Start.
                    self.mode = "idle"
                    self.paused_mode = None
                    self.remaining_seconds = self.work_seconds
                    self.update_display()
                    self.set_status()
                    if was_long_break:
                        messagebox.showinfo(
                            "Long Break Complete",
                            "Your long break is complete. Press Start when you are ready for the next work session."
                        )
                    else:
                        messagebox.showinfo(
                            "Break Complete",
                            "Your break is complete. Press Start when you are ready for the next work session."
                        )


if __name__ == "__main__":
    # Create app window and start Tkinter event loop.
    root = tk.Tk()
    app = PomodoroApp(root)
    root.mainloop()
