using PomodoroTimer.Spike.Core.Models;

namespace PomodoroTimer.Spike.Core.Services;

// Window controls and tray items use exactly the same operations.
public sealed class SpikeActions(TestTimer timer, Action open, Action exit,
    Action<NotificationRequest> notify)
{
    public void Start() => timer.Start();
    public void TogglePause() => timer.TogglePause();
    public void Open() => open();
    public void Exit() => exit();
    public void SendTestNotification() => notify(new(
        "Pomodoro Timer 技术验证", "这是 Windows 原生本地通知。点击打开原有窗口。", "manual"));
    public void TimerCompleted() => notify(new(
        "30 秒测试计时结束", "后台计时已结束。点击打开 Pomodoro Timer 技术预览。", "timer"));
}
