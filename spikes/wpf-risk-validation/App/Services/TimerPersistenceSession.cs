using System;
using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Core.Services;
using PomodoroTimer.Spike.Infrastructure;

namespace PomodoroTimer.Spike.Services;

// Owned by the dispatcher, like TestTimer. Ordinary ticks refresh UI but never write a snapshot.
public sealed class TimerPersistenceSession : IDisposable
{
    private readonly TestTimer timer;
    private readonly TimerStateStore store;
    private readonly Action<PersistenceDiagnostic> report;
    private readonly bool canSave;
    private TimerState lastState;
    private bool disposed;
    private bool saving;
    private CheckpointResult? finalResult;

    public TimerPersistenceSession(TestTimer timer, TimerStateStore store, Action<PersistenceDiagnostic> report)
    {
        this.timer = timer;
        this.store = store;
        this.report = DiagnosticReporter.Safe(report);
        var loaded = store.Load();
        canSave = loaded.CanSave;
        timer.Restore(loaded.Value);
        lastState = timer.State;
        timer.Changed += OnChanged;
        if (loaded.Value.State != timer.State) Checkpoint();
        if (loaded.Value.State is TimerState.Running or TimerState.Paused)
            this.report(timer.State == TimerState.Completed
                ? new(DiagnosticLevel.Information, "timer.restored.completed", "已恢复零剩余时间的完成状态，不重发完成通知。")
                : new(DiagnosticLevel.Information, "timer.restored.paused",
                    "已恢复计时快照并保持暂停；关机、退出和睡眠期间的时间不会自动累加。"));
    }

    private void OnChanged()
    {
        if (timer.State == lastState) return;
        lastState = timer.State;
        Checkpoint();
    }

    public CheckpointResult Checkpoint()
    {
        if (disposed) return finalResult ?? new(CheckpointStatus.InProgress);
        return SaveCheckpoint();
    }

    private CheckpointResult SaveCheckpoint()
    {
        if (saving) return new(CheckpointStatus.InProgress);
        if (!canSave) return new(CheckpointStatus.Disabled);
        saving = true;
        try
        {
            store.Save(timer.CaptureSnapshot());
            return new(CheckpointStatus.Saved);
        }
        catch (PersistenceException error)
        {
            report(new(DiagnosticLevel.Error, "timer.checkpoint.failed",
                 "计时器仍可运行，但本次状态未能保存。重启可能恢复到上一份快照。", error));
            return new(CheckpointStatus.Failed, error);
        }
        finally { saving = false; }
    }

    public CheckpointResult Complete()
    {
        if (disposed) return finalResult ?? new(CheckpointStatus.InProgress);
        if (saving) return new(CheckpointStatus.InProgress);
        disposed = true;
        timer.Changed -= OnChanged;
        try { finalResult = SaveCheckpoint(); }
        catch (Exception error) { finalResult = new(CheckpointStatus.Failed, error); }
        return finalResult;
    }

    public void Dispose() => Complete();
}
