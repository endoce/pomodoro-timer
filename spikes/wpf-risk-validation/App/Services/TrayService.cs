using System;
using System.Drawing;
using System.Windows.Threading;
using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Core.Services;
using Forms = System.Windows.Forms;

namespace PomodoroTimer.Spike.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.ToolStripMenuItem pause = new("暂停 / 继续");
    private readonly Icon image = (Icon)SystemIcons.Information.Clone();
    private readonly Dispatcher dispatcher;

    public TrayService(SpikeActions actions, Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        menu.Items.Add("打开 (Open)", null, (_, _) => Dispatch(actions.Open));
        pause.Click += (_, _) => Dispatch(actions.TogglePause);
        menu.Items.Add(pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 (Exit)", null, (_, _) => Dispatch(actions.Exit));
        icon = new Forms.NotifyIcon { Icon = image, Text = "Pomodoro Timer 技术预览", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => Dispatch(actions.Open);
    }

    private void Dispatch(Action action) => dispatcher.BeginInvoke(action);

    public void Update(TestTimer timer)
    {
        pause.Enabled = timer.State is TimerState.Running or TimerState.Paused;
        pause.Text = timer.State == TimerState.Paused ? "继续 (Resume)" : "暂停 (Pause)";
        icon.Text = $"Pomodoro 技术预览 · {timer.State} · {Math.Ceiling(timer.Remaining.TotalSeconds):00} 秒";
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
        image.Dispose();
    }
}
