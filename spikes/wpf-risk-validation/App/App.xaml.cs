using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Core.Services;
using PomodoroTimer.Spike.Infrastructure;
using PomodoroTimer.Spike.Models;
using PomodoroTimer.Spike.Services;
using PomodoroTimer.Spike.ViewModels;
using PomodoroTimer.Spike.Views;

namespace PomodoroTimer.Spike;

public partial class App : Application
{
    private AppInstance? instance;
    private NotificationSession? notifications;
    private TrayService? tray;
    private ThemeService? theme;
    private DispatcherTimer? wakeup;
    private TestTimer? timer;
    private MainViewModel? model;
    private MainWindow? window;
    private bool exiting;
    private bool pendingActivation;

    protected override async void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        try
        {
            if (args.Args.Contains("--unregister-notifications"))
            {
                instance = AppInstance.FindOrRegisterForKey("PomodoroTimerSpike.WpfRiskValidation");
                if (!instance.IsCurrent)
                {
                    instance = null;
                    throw new InvalidOperationException("请先从托盘退出原有应用，再清理通知注册。");
                }
                // Uninstall must use SDK cleanup, not hand-written COM registry deletion.
                AppNotificationManager.Default.UnregisterAll();
                SpikeLog.Write("Uninstall: UnregisterAll completed");
                Shutdown(0);
                return;
            }
            if (args.Args.Contains("--self-test"))
            {
                var output = args.Args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
                _ = new MainWindow();
                var built = WindowsNotificationBackend.Build(new("技术测试", "仅构建，不显示、不注册。", "self-test"));
                if (!built.Payload.Contains("技术测试")) throw new InvalidOperationException("Notification payload missing title");
                if (output != null) File.WriteAllText(output,
                    "PASS: published WPF XAML initialization and native AppNotificationBuilder\n"
                    + "NOT TESTED: notification display/registration/activation, tray, Windows 11 desktop\n"
                    + Diagnostics.Describe() + "\n" + Diagnostics.NativeModules());
                Shutdown(0);
                return;
            }

            SpikeLog.Write("Process startup: " + Diagnostics.Describe().Replace('\n', ' '));
            theme = new();
            timer = new(new StopwatchClock());
            var backend = new WindowsNotificationBackend();
            notifications = new(backend, () => QueueActivation("NotificationInvoked"), Report);

            // Registration precedes GetActivatedEventArgs; no STA-blocking redirect.
            var registered = notifications.Initialize();
            SpikeLog.Write(Diagnostics.NativeModules());
            instance = AppInstance.FindOrRegisterForKey("PomodoroTimerSpike.WpfRiskValidation");
            if (!instance.IsCurrent)
            {
                if (!registered) throw new InvalidOperationException("Cannot safely redirect without notification initialization");
                var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                await instance.RedirectActivationToAsync(activation);
                SpikeLog.Write("Secondary launch redirected to existing instance; no second window created");
                instance = null;
                Shutdown(0);
                return;
            }
            instance.Activated += OnActivated;
            if (registered)
            {
                var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                SpikeLog.Write($"Startup activation kind={activation.Kind}");
                if (activation.Kind == ExtendedActivationKind.AppNotification) pendingActivation = true;
            }

            var actions = new SpikeActions(timer, () => QueueActivation("Open"), ExitPreview,
                request => notifications.Send(request));
            window = new();
            MainWindow = window;
            model = new(timer, actions, window.Hide);
            window.DataContext = model;
            model.ReportNotification(registered ? "通知已注册。发送成功只表示提交，不保证横幅显示。" : "通知注册失败，计时与窗口仍可测试；详见日志。");
            tray = new(actions, Dispatcher);
            timer.Changed += Refresh;
            timer.Completed += actions.TimerCompleted;
            wakeup = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
                (_, _) => timer.Tick(), Dispatcher);
            SystemEvents.PowerModeChanged += OnPowerChanged;
            Refresh();
            window.Show();
            if (pendingActivation) QueueActivation("Queued startup notification");
        }
        catch (Exception error)
        {
            SpikeLog.Write($"Startup failure {error.GetType().Name} HRESULT=0x{error.HResult:X8}: {error.Message}");
            if (!args.Args.Contains("--self-test") && !args.Args.Contains("--unregister-notifications"))
                MessageBox.Show($"技术预览启动失败：{error.Message}\n日志：{SpikeLog.DirectoryPath}", "技术验证失败");
            Shutdown(1);
        }
    }

    private void OnActivated(object? sender, AppActivationArguments args) => QueueActivation("AppInstance.Activated");

    private void QueueActivation(string reason)
    {
        if (exiting || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (exiting) return;
            if (window == null) { pendingActivation = true; return; }
            pendingActivation = false;
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            var activated = window.Activate();
            SpikeLog.Write($"Window activation reason={reason}, Activate returned={activated}");
            model?.ReportNotification($"窗口激活：{reason} · PID {Environment.ProcessId}。请检查是否为原有窗口。");
        }));
    }

    private void Report(string message)
    {
        SpikeLog.Write(message);
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(() => model?.ReportNotification(message)));
    }

    private void Refresh() { model?.Refresh(); tray?.Update(timer!); }

    private void OnPowerChanged(object sender, PowerModeChangedEventArgs args)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (args.Mode == PowerModes.Suspend && timer?.State == TimerState.Running)
            {
                timer.Pause();
                Report("系统挂起：计时暂停，恢复后请手动继续。该行为仍需25H2桌面验收。");
            }
            if (args.Mode == PowerModes.Resume) SpikeLog.Write("System resume received; no automatic resume");
        }));
    }

    private void ExitPreview()
    {
        exiting = true;
        if (window != null) window.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs args)
    {
        exiting = true;
        wakeup?.Stop();
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        tray?.Dispose();
        theme?.Dispose();
        notifications?.Dispose();
        if (instance?.IsCurrent == true)
        {
            instance.Activated -= OnActivated;
            instance.UnregisterKey();
        }
        SpikeLog.Write("Exit: timer stopped, tray disposed, notification session released");
        base.OnExit(args);
    }
}
