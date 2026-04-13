import tkinter as tk

# A single pomodoro session is 25 minutes.
TOTAL_SECONDS = 25 * 60


class PomodoroApp:
    def __init__(self, root):
        # Keep a reference to the main Tkinter window.
        self.root = root
        self.root.title("Pomodoro Timer")
        self.root.geometry("320x220")

        # remaining_seconds stores how much time is left in the session.
        self.remaining_seconds = TOTAL_SECONDS

        # timer_job stores the scheduled `after` callback id.
        # It is None when no countdown callback is currently scheduled.
        self.timer_job = None

        # Large label in the center that shows time like 25:00.
        self.time_label = tk.Label(
            root,
            text=self.format_time(self.remaining_seconds),
            font=("Arial", 32)
        )
        self.time_label.pack(pady=25)

        # Status label explains current timer state for beginners.
        self.status_label = tk.Label(root, text="Status: Idle", font=("Arial", 12))
        self.status_label.pack(pady=(0, 10))

        # Use one frame so the three control buttons stay on one row.
        self.button_frame = tk.Frame(root)
        self.button_frame.pack()

        # Start begins or resumes the countdown.
        self.start_button = tk.Button(
            self.button_frame,
            text="Start",
            width=10,
            command=self.start_timer
        )
        self.start_button.grid(row=0, column=0, padx=4)

        # Pause stops the scheduled countdown callback with after_cancel.
        self.pause_button = tk.Button(
            self.button_frame,
            text="Pause",
            width=10,
            command=self.pause_timer
        )
        self.pause_button.grid(row=0, column=1, padx=4)

        # Reset stops any running timer and restores 25:00.
        self.reset_button = tk.Button(
            self.button_frame,
            text="Reset",
            width=10,
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

    def set_status(self, status_text):
        # Keep status updates in one place for clarity.
        self.status_label.config(text=f"Status: {status_text}")

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
