using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PomodoroTimer.Spike.Infrastructure;

// Cooperating stores share a mutex in the current OS user's/session's namespace.
// FullPath is lexical: symlink/hardlink aliases and non-cooperating writers are outside the contract.
internal sealed class StorageTransaction : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    [ThreadStatic] private static HashSet<string>? owned;
    private readonly Mutex mutex;
    private readonly string name;
    private bool disposed;

    private StorageTransaction(Mutex mutex, string name)
    {
        this.mutex = mutex;
        this.name = name;
    }

    public static string Normalize(string file) => Path.GetFullPath(file);

    public static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.FromMilliseconds(1) || timeout > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(timeout), "事务等待必须在 1ms 到 5s 之间。");
    }

    public static StorageTransaction Acquire(string file, TimeSpan timeout, Action<PersistenceDiagnostic> report)
    {
        var identity = OperatingSystem.IsWindows() ? file.ToUpperInvariant() : file;
        var name = "PomodoroTimer.Portable.Storage." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        owned ??= new(StringComparer.Ordinal);
        // Mutexes are reentrant; reject callbacks that would interleave a nested transaction instead.
        if (owned.Contains(name)) throw new PersistenceException("拒绝同路径嵌套持久化事务。");

        Mutex? mutex = null;
        var acquired = false;
        try
        {
            mutex = new(false, name);
            var abandoned = false;
            try { acquired = mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { acquired = true; abandoned = true; }
            if (!acquired) throw new PersistenceException($"同路径持久化事务等待超时：{file}。已有文件不会被覆盖。");
            owned.Add(name);
            if (abandoned)
                DiagnosticReporter.Safe(report)(new(DiagnosticLevel.Warning, "storage.transaction.abandoned",
                    "前一持久化进程异常退出；已重新取得事务锁，将重新校验主文件与备份。"));
            return new(mutex, name);
        }
        catch (Exception error)
        {
            if (acquired) { owned.Remove(name); mutex!.ReleaseMutex(); }
            mutex?.Dispose();
            throw new PersistenceException($"无法取得持久化事务所有权：{file}。不会直接覆盖或回退。", error);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { mutex.ReleaseMutex(); }
        finally { owned!.Remove(name); mutex.Dispose(); }
    }
}
