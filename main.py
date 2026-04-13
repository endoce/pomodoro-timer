import tkinter as tk

# A single pomodoro session is 25 minutes.
TOTAL_SECONDS = 25 * 60


class PomodoroApp:
    def __init__(self, root):
        # Keep a reference to the main Tkinter window.
        self.root = root
        self.root.title("Pomodoro Timer")
        self.root.geometry("300x200")

        # remaining_seconds stores how much time is left in the session.
        self.remaining_seconds = TOTAL_SECONDS

        # timer_job stores the scheduled `after` callback id.
        # It is None when the timer is not running.
        self.timer_job = None

        # Large label in the center that shows time like 25:00.
        self.time_label = tk.Label(
            root,
            text=self.format_time(self.remaining_seconds),
            font=("Arial", 32)
        )
        self.time_label.pack(pady=30)

        # Start button to begin the countdown.
        self.start_button = tk.Button(
            root,
            text="Start",
            width=10,
            command=self.start_timer
        )
        self.start_button.pack()

    def format_time(self, seconds):
        # Convert total seconds into MM:SS format.
        minutes = seconds // 60
        remaining_seconds = seconds % 60
        return f"{minutes:02d}:{remaining_seconds:02d}"

    def update_display(self):
        # Refresh the label text using the latest remaining_seconds value.
        self.time_label.config(text=self.format_time(self.remaining_seconds))

    def start_timer(self):
        # Start only once. If already running, do nothing.
        if self.timer_job is None:
            self.countdown()

    def countdown(self):
        # Decrease one second, update UI, and schedule the next tick.
        if self.remaining_seconds > 0:
            self.remaining_seconds -= 1
            self.update_display()
            self.timer_job = self.root.after(1000, self.countdown)
        else:
            # Reaching 00:00 ends the timer loop.
            self.timer_job = None


if __name__ == "__main__":
    # Create app window and start Tkinter event loop.
    root = tk.Tk()
    app = PomodoroApp(root)
    root.mainloop()
