namespace PomodoroTimer.Spike.Core.Models;

public enum TimerState { Ready, Running, Paused, Completed }

public sealed record NotificationRequest(string Title, string Body, string Source);
