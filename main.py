import tkinter as tk

# A single pomodoro session is 25 minutes.
TOTAL_SECONDS = 25 * 60
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
        self.root.geometry("380x280")
        self.root.configure(bg=BG_COLOR)

        # remaining_seconds stores how much time is left in the session.
        self.remaining_seconds = TOTAL_SECONDS

        # timer_job stores the scheduled `after` callback id.
        # It is None when no countdown callback is currently scheduled.
        self.timer_job = None

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

        # Use one frame so the three control buttons stay on one row.
        self.button_frame = tk.Frame(self.main_frame, bg=CARD_COLOR)
        self.button_frame.pack()

        # Start begins or resumes the countdown.
        self.start_button = tk.Button(
            self.button_frame,
            text="Start",
            width=11,
            bg="#4CAF6D",
            fg="white",
            activebackground="#43A861",
            activeforeground="white",
            font=("Arial", 11, "bold"),
            relief="raised",
            bd=2,
            command=self.start_timer
        )
        self.start_button.grid(row=0, column=0, padx=6)

        # Pause stops the scheduled countdown callback with after_cancel.
        self.pause_button = tk.Button(
            self.button_frame,
            text="Pause",
            width=11,
            bg="#F4A340",
            fg="white",
            activebackground="#E39230",
            activeforeground="white",
            font=("Arial", 11, "bold"),
            relief="raised",
            bd=2,
            command=self.pause_timer
        )
        self.pause_button.grid(row=0, column=1, padx=6)

        # Reset stops any running timer and restores 25:00.
        self.reset_button = tk.Button(
            self.button_frame,
            text="Reset",
            width=11,
            bg="#7CC8F8",
            fg="white",
            activebackground="#6EBDEB",
            activeforeground="white",
            font=("Arial", 11, "bold"),
            relief="raised",
            bd=2,
            command=self.reset_timer
        )
        self.reset_button.grid(row=0, column=2, padx=6)

    def format_time(self, seconds):
        # Convert total seconds into MM:SS format.
        minutes = seconds // 60
        remaining_seconds = seconds % 60
        return f"{minutes:02d}:{remaining_seconds:02d}"

    def update_display(self):
        # Refresh the label text using the latest remaining_seconds value.
        self.time_label.config(text=self.format_time(self.remaining_seconds))

    def set_status(self, status_text):
        # Keep status updates in one place for clarity.
        status_colors = {
            "Idle": STATUS_IDLE_COLOR,
            "Running": STATUS_RUNNING_COLOR,
            "Paused": STATUS_PAUSED_COLOR,
        }
        self.status_label.config(
            text=f"Status: {status_text}",
            fg=status_colors.get(status_text, STATUS_IDLE_COLOR)
        )

    def start_timer(self):
        # Prevent parallel timers by starting only when no callback is scheduled.
        if self.timer_job is None and self.remaining_seconds > 0:
            self.set_status("Running")
            self.countdown()

    def pause_timer(self):
        # Pause by cancelling the next scheduled callback.
        if self.timer_job is not None:
            self.root.after_cancel(self.timer_job)
            self.timer_job = None
            self.set_status("Paused")

    def reset_timer(self):
        # Reset should also stop a running timer before restoring initial time.
        if self.timer_job is not None:
            self.root.after_cancel(self.timer_job)
            self.timer_job = None

        self.remaining_seconds = TOTAL_SECONDS
        self.update_display()
        self.set_status("Idle")

    def countdown(self):
        # Keep the existing countdown flow: decrement, update display, schedule next tick.
        if self.remaining_seconds > 0:
            self.remaining_seconds -= 1
            self.update_display()
            self.timer_job = self.root.after(1000, self.countdown)
        else:
            # Reaching 00:00 ends the timer loop.
            self.timer_job = None
            self.set_status("Idle")


if __name__ == "__main__":
    # Create app window and start Tkinter event loop.
    root = tk.Tk()
    app = PomodoroApp(root)
    root.mainloop()
