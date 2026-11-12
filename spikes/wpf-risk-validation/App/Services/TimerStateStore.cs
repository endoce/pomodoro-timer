using System;
using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Core.Services;
using PomodoroTimer.Spike.Infrastructure;

namespace PomodoroTimer.Spike.Services;

public sealed class TimerStateStore
{
    private readonly AtomicJsonStore<TimerSnapshot> store;

    public TimerStateStore(PortablePaths paths, Action<PersistenceDiagnostic> report,
        IAtomicFileCommitter? committer = null, TimeSpan? transactionTimeout = null) => store = new(paths.StateFile,
            () => new()
            {
                State = TimerState.Ready,
                DurationTicks = TestTimer.Duration.Ticks,
                RemainingTicks = TestTimer.Duration.Ticks
            }, TestTimer.ValidateSnapshot, report, committer, transactionTimeout);

    public string BackupFile => store.BackupFile;
    public StoreReadResult<TimerSnapshot> Load() => store.Load();
    public void Save(TimerSnapshot snapshot) => store.Save(snapshot);
    public void RestoreBackup() => store.RestoreBackup();
}
