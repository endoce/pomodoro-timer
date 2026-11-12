using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Core.Services;
using PomodoroTimer.Spike.Infrastructure;
using PomodoroTimer.Spike.Models;
using PomodoroTimer.Spike.Services;

namespace PomodoroTimer.Spike.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly TestTimer timer;
    private readonly TimerPersistenceSession persistence;
    private string persistenceStatus = "";
    private bool disposed;
    private string notificationStatus = "正在初始化通知…";
    public MainViewModel(TestTimer timer, SpikeActions actions, Action hide)
    {
        this.timer = timer;
        StartCommand = new(actions.Start, () => timer.State is TimerState.Ready or TimerState.Completed);
        PauseCommand = new(actions.TogglePause, () => timer.State is TimerState.Running or TimerState.Paused);
        NotifyCommand = new(actions.SendTestNotification);
        HideCommand = new(hide);
        var paths = PortablePaths.Default;
        paths.Initialize(ReportPersistence);
        var settings = new SettingsStore(paths, ReportPersistence);
        var loaded = settings.Load();
        if (loaded.CanSave && !File.Exists(paths.SettingsFile)) settings.Save(loaded.Value);
        persistence = new(timer, new TimerStateStore(paths, ReportPersistence), ReportPersistence);
        if (ApplicationLog.LastFailure is { } warning) ReportLogFailure(warning);
        ApplicationLog.Failed += ReportLogFailure;
        // App.OnExit explicitly completes persistence before releasing its single-instance key.
    }

    public string TimeText => $"00:{Math.Ceiling(timer.Remaining.TotalSeconds):00}";
    public double Progress => (1 - timer.Remaining.TotalSeconds / 30) * 100;
    public string Status => timer.State switch
    {
        TimerState.Running => "计时中", TimerState.Paused => "已暂停",
        TimerState.Completed => "已完成", _ => "等待开始"
    };
    public string PauseText => timer.State == TimerState.Paused ? "继续 (Resume)" : "暂停 (Pause)";
    public string DiagnosticsText => Diagnostics.Describe() + persistenceStatus;
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

    private void ReportPersistence(PersistenceDiagnostic diagnostic)
    {
        ApplicationLog.Write(diagnostic);
        ShowDiagnostic(diagnostic);
    }

    private void ReportLogFailure(PersistenceDiagnostic diagnostic)
    {
        if (Application.Current.Dispatcher.CheckAccess()) ShowDiagnostic(diagnostic);
        else if (!Application.Current.Dispatcher.HasShutdownStarted)
            Application.Current.Dispatcher.BeginInvoke(new Action(() => ShowDiagnostic(diagnostic)));
    }

    private void ShowDiagnostic(PersistenceDiagnostic diagnostic)
    {
        // Reuse the existing diagnostics region; do not add a settings/recovery UI in this phase.
        var line = $"\n[{diagnostic.Level}] {diagnostic.Message}";
        if (!persistenceStatus.Contains(line, StringComparison.Ordinal)) persistenceStatus += line;
        PropertyChanged?.Invoke(this, new(nameof(DiagnosticsText)));
    }

    public CheckpointResult CompletePersistence()
    {
        if (disposed) return persistence.Complete();
        disposed = true;
        try { return persistence.Complete(); }
        finally { ApplicationLog.Failed -= ReportLogFailure; }
    }

    public void Dispose() => CompletePersistence();
}
