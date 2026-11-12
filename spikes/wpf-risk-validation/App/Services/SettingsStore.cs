using System;
using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Infrastructure;

namespace PomodoroTimer.Spike.Services;

public sealed class SettingsStore
{
    private readonly AtomicJsonStore<ApplicationSettings> store;

    public SettingsStore(PortablePaths paths, Action<PersistenceDiagnostic> report,
        IAtomicFileCommitter? committer = null, TimeSpan? transactionTimeout = null) => store = new(paths.SettingsFile,
            () => new(), Validate, report, committer, transactionTimeout);

    public string BackupFile => store.BackupFile;
    public StoreReadResult<ApplicationSettings> Load() => store.Load();
    public void Save(ApplicationSettings settings) => store.Save(settings);
    public void RestoreBackup() => store.RestoreBackup();

    private static void Validate(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != ApplicationSettings.CurrentSchemaVersion)
            throw new ArgumentException("不支持的设置 schemaVersion。", nameof(settings));
    }
}
