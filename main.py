import tkinter as tk

# Short durations so mode switching is easy to test.
WORK_SECONDS = 30
BREAK_SECONDS = 10
BG_COLOR = "#F3FFF8"
CARD_COLOR = "#FFFFFF"
TIMER_COLOR = "#FF6F61"
STATUS_IDLE_COLOR = "#5C6F7B"
STATUS_RUNNING_COLOR = "#2E9E5B"
STATUS_PAUSED_COLOR = "#F39C3D"


class PomodoroApp:
    def __init__(self, root):
        # Keep a reference to the main Tkinter window.
        self.root = root
        self.root.title("Pomodoro Timer")
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
        self.remaining_seconds = WORK_SECONDS

        # timer_job stores the scheduled `after` callback id.
        # It is None when no countdown callback is currently scheduled.
        self.timer_job = None

        # Basic in-memory statistics.
        # completed_sessions counts finished work sessions.
        # focus_seconds tracks total completed work time in seconds.
        self.completed_sessions = 0
        self.focus_seconds = 0

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
            text="Completed: 0 | Focus: 0s",
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
            self.remaining_seconds = WORK_SECONDS
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
        self.remaining_seconds = WORK_SECONDS
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
                self.focus_seconds += WORK_SECONDS
                self.update_stats_display()

                self.mode = "break"
                self.remaining_seconds = BREAK_SECONDS
                self.set_status()
                self.update_display()
                self.countdown()
            elif self.mode == "break":
                self.mode = "idle"
                self.paused_mode = None
                self.remaining_seconds = WORK_SECONDS
                self.update_display()
                self.set_status()


if __name__ == "__main__":
    # Create app window and start Tkinter event loop.
    root = tk.Tk()
    app = PomodoroApp(root)
    root.mainloop()
