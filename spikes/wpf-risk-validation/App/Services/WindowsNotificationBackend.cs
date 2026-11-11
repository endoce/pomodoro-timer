using System;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Core.Services;
using PomodoroTimer.Spike.Infrastructure;

namespace PomodoroTimer.Spike.Services;

public sealed class WindowsNotificationBackend : INotificationBackend
{
    public event Action? Invoked;

    public void Register()
    {
        // The SDK performs unpackaged COM registration. Do not duplicate it.
        AppNotificationManager.Default.NotificationInvoked += OnInvoked;
        try { AppNotificationManager.Default.Register(); }
        catch
        {
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
            throw;
        }
    }

    public static AppNotification Build(NotificationRequest request) => new AppNotificationBuilder()
        .AddArgument("action", "open")
        .AddArgument("source", request.Source)
        .AddText(request.Title)
        .AddText(request.Body)
        .BuildNotification();

    public void Show(NotificationRequest request)
    {
        var setting = AppNotificationManager.Default.Setting;
        SpikeLog.Write($"Notification setting={setting}, source={request.Source}");
        if (setting != AppNotificationSetting.Enabled)
            throw new InvalidOperationException($"Windows notification setting is {setting}");
        var notification = Build(request);
        AppNotificationManager.Default.Show(notification);
        if (notification.Id == 0) throw new InvalidOperationException("Windows returned notification ID 0");
        SpikeLog.Write($"Notification submitted ID={notification.Id}; desktop visibility NOT verified");
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        SpikeLog.Write("NotificationInvoked received (not equivalent to process startup)");
        if (args.Arguments.TryGetValue("action", out var action) && action == "open") Invoked?.Invoke();
    }

    public void Unregister()
    {
        AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
        AppNotificationManager.Default.Unregister();
        SpikeLog.Write("AppNotificationManager.Unregister completed");
    }
}
