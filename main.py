import tkinter as tk

TOTAL_SECONDS = 25 * 60


class PomodoroApp:
    def __init__(self, root):
        self.root = root
        self.root.title("Pomodoro Timer")
        self.root.geometry("300x200")

        self.remaining_seconds = TOTAL_SECONDS
        self.timer_job = None

        self.time_label = tk.Label(
            root,
            text=self.format_time(self.remaining_seconds),
            font=("Arial", 32)
        )
        self.time_label.pack(pady=30)

        self.start_button = tk.Button(
            root,
            text="开始",
            width=10,
            command=self.start_timer
        )
        self.start_button.pack()

    def format_time(self, seconds):
        minutes = seconds // 60
        remaining_seconds = seconds % 60
        return f"{minutes:02d}:{remaining_seconds:02d}"

    def update_display(self):
        self.time_label.config(text=self.format_time(self.remaining_seconds))

    def start_timer(self):
        if self.timer_job is None:
            self.countdown()

    def countdown(self):
        if self.remaining_seconds > 0:
            self.remaining_seconds -= 1
            self.update_display()
            self.timer_job = self.root.after(1000, self.countdown)
        else:
            self.timer_job = None


if __name__ == "__main__":
    root = tk.Tk()
    app = PomodoroApp(root)
    root.mainloop()
