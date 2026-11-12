using System;
using System.Globalization;
using System.IO;

namespace PomodoroTimer.Spike.Infrastructure;

public sealed class PortablePaths
{
    public static PortablePaths Default { get; } = new(AppContext.BaseDirectory);

    // Tests may supply an absolute root. Production always uses Default, never the working directory.
    public PortablePaths(string applicationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        if (!Path.IsPathFullyQualified(applicationRoot))
            throw new ArgumentException("应用根目录必须是绝对路径。", nameof(applicationRoot));
        ApplicationRoot = Path.GetFullPath(applicationRoot);
        DataDirectory = Path.Combine(ApplicationRoot, "Data");
        LogsDirectory = Path.Combine(ApplicationRoot, "Logs");
    }

    public string ApplicationRoot { get; }
    public string DataDirectory { get; }
    public string LogsDirectory { get; }
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public string StateFile => Path.Combine(DataDirectory, "state.json");

    public string LogFile(DateOnly utcDay, int sequence) => Path.Combine(LogsDirectory,
        string.Create(CultureInfo.InvariantCulture, $"pomodoro-{utcDay:yyyy-MM-dd}-{sequence:D4}.log"));

    public void Initialize(Action<PersistenceDiagnostic> report)
    {
        EnsureDataWritable();
        TryEnsureLogsWritable(report);
    }

    public void EnsureDataWritable()
    {
        try { ProbeWritable(DataDirectory); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new PersistenceException(
                $"Data 目录不可写：{DataDirectory}。请将整个应用目录移到当前用户可写的位置；不会请求管理员权限或回退到其他目录。已有数据未被覆盖。",
                error);
        }
    }

    public bool TryEnsureLogsWritable(Action<PersistenceDiagnostic> report)
    {
        try { ProbeWritable(LogsDirectory); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DiagnosticReporter.Safe(report)(new(DiagnosticLevel.Warning, "logs.unwritable",
                $"Logs 目录不可写：{LogsDirectory}。计时器仍可运行，但日志无法保存。", error));
            return false;
        }
    }

    private static void ProbeWritable(string directory)
    {
        Directory.CreateDirectory(directory);
        // CreateNew never replaces user data; DeleteOnClose also handles a failed write.
        var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}.tmp");
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 1, FileOptions.DeleteOnClose | FileOptions.WriteThrough);
        stream.WriteByte(0);
        stream.Flush(flushToDisk: true);
    }
}
