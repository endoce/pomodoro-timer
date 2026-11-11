using System;
using System.ComponentModel;
using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Core.Services;
using PomodoroTimer.Spike.Infrastructure;
using PomodoroTimer.Spike.Models;

namespace PomodoroTimer.Spike.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly TestTimer timer;
    private string notificationStatus = "正在初始化通知…";
    public MainViewModel(TestTimer timer, SpikeActions actions, Action hide)
    {
        this.timer = timer;
        StartCommand = new(actions.Start, () => timer.State is TimerState.Ready or TimerState.Completed);
        PauseCommand = new(actions.TogglePause, () => timer.State is TimerState.Running or TimerState.Paused);
        NotifyCommand = new(actions.SendTestNotification);
        HideCommand = new(hide);
    }

    public string TimeText => $"00:{Math.Ceiling(timer.Remaining.TotalSeconds):00}";
    public double Progress => (1 - timer.Remaining.TotalSeconds / 30) * 100;
    public string Status => timer.State switch
    {
        TimerState.Running => "计时中", TimerState.Paused => "已暂停",
        TimerState.Completed => "已完成", _ => "等待开始"
    };
    public string PauseText => timer.State == TimerState.Paused ? "继续 (Resume)" : "暂停 (Pause)";
    public string DiagnosticsText => Diagnostics.Describe();
    public string NotificationStatus => notificationStatus;
    public RelayCommand StartCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand NotifyCommand { get; }
    public RelayCommand HideCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new(null));
        StartCommand.Refresh();
        PauseCommand.Refresh();
    }
    public void ReportNotification(string message)
    {
        notificationStatus = message;
        PropertyChanged?.Invoke(this, new(nameof(NotificationStatus)));
    }
}
