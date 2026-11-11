using System.Diagnostics;
using PomodoroTimer.Spike.Core.Models;

namespace PomodoroTimer.Spike.Core.Services;

public interface IMonotonicClock { TimeSpan Now { get; } }

public sealed class StopwatchClock : IMonotonicClock
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    public TimeSpan Now => clock.Elapsed;
}

// All operations belong to one owner (the WPF dispatcher). Tick is a wake-up,
// not the source of elapsed time. Hiding the window does not change this owner.
public sealed class TestTimer(IMonotonicClock clock)
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);
    private TimeSpan elapsed;
    private TimeSpan startedAt;
    public TimerState State { get; private set; } = TimerState.Ready;
    public event Action? Changed;
    public event Action? Completed;

    public TimeSpan Remaining => TimeSpan.FromTicks(Math.Max(0,
        (Duration - elapsed - (State == TimerState.Running ? clock.Now - startedAt : TimeSpan.Zero)).Ticks));

    public void Start()
    {
        if (State is TimerState.Running or TimerState.Paused) return;
        elapsed = TimeSpan.Zero;
        startedAt = clock.Now;
        State = TimerState.Running;
        Changed?.Invoke();
    }

    public void Pause()
    {
        if (State != TimerState.Running) return;
        elapsed += clock.Now - startedAt;
        if (elapsed >= Duration) { Finish(); return; }
        State = TimerState.Paused;
        Changed?.Invoke();
    }

    public void Resume()
    {
        if (State != TimerState.Paused) return;
        startedAt = clock.Now;
        State = TimerState.Running;
        Changed?.Invoke();
    }

    public void TogglePause()
    {
        if (State == TimerState.Running) Pause();
        else if (State == TimerState.Paused) Resume();
    }

    public void Tick()
    {
        if (State != TimerState.Running) return;
        if (Remaining > TimeSpan.Zero) { Changed?.Invoke(); return; }
        Finish();
    }

    private void Finish()
    {
        elapsed = Duration;
        State = TimerState.Completed;
        Changed?.Invoke();
        Completed?.Invoke();
    }
}
