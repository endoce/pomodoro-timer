using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PomodoroTimer.Spike.Infrastructure;

// Injection is limited to the commit boundary, so interruption tests still exercise real file I/O.
public interface IAtomicFileCommitter
{
    void Commit(string temporaryFile, string destinationFile, string? backupFile);
}

public sealed class AtomicFileCommitter : IAtomicFileCommitter
{
    public void Commit(string temporaryFile, string destinationFile, string? backupFile)
    {
        if (File.Exists(destinationFile))
            File.Replace(temporaryFile, destinationFile, backupFile);
        else
            File.Move(temporaryFile, destinationFile, overwrite: false);
    }
}

public sealed record StoreReadResult<T>(T Value, bool CanSave, bool FromBackup,
    IReadOnlyList<PersistenceDiagnostic> Diagnostics);

internal sealed class AtomicJsonStore<T> where T : class
{
    private const int MaximumDocumentBytes = 64 * 1024;
    private readonly TimeSpan transactionTimeout;
    private readonly string file;
    private readonly Func<T> defaults;
    private readonly Action<T> validate;
    private readonly Action<PersistenceDiagnostic> report;
    private readonly IAtomicFileCommitter committer;
    private readonly JsonSerializerOptions options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter<Core.Models.TimerState>(allowIntegerValues: false) }
    };

    public AtomicJsonStore(string file, Func<T> defaults, Action<T> validate,
        Action<PersistenceDiagnostic> report, IAtomicFileCommitter? committer, TimeSpan? transactionTimeout = null)
    {
        this.file = StorageTransaction.Normalize(file);
        this.transactionTimeout = transactionTimeout ?? StorageTransaction.DefaultTimeout;
        StorageTransaction.ValidateTimeout(this.transactionTimeout);
        this.defaults = defaults;
        this.validate = validate;
        this.report = DiagnosticReporter.Safe(report);
        this.committer = committer ?? new AtomicFileCommitter();
    }

    public string BackupFile => file + ".bak";

    public StoreReadResult<T> Load()
    {
        var diagnostics = new List<PersistenceDiagnostic>();
        try
        {
            using var transaction = StorageTransaction.Acquire(file, transactionTimeout, report);
            var primary = Read(file);
            if (primary.Value is not null)
            {
                var existingBackup = Read(BackupFile);
                if (existingBackup.Error is { } backupError) Emit(backupError, diagnostics);
                return new(primary.Value, existingBackup.Missing || existingBackup.Value is not null, false, diagnostics);
            }
            if (primary.Error is not null) Emit(primary.Error, diagnostics);
            // A future schema must not be replaced or interpreted as an older backup.
            if (primary.FutureSchema) return new(defaults(), false, false, diagnostics);

            var backup = Read(BackupFile);
            if (backup.Value is not null)
            {
                Emit(new(DiagnosticLevel.Warning, "storage.backup.loaded",
                    $"已读取备份：{BackupFile}。原文件保持不变，保存已禁用；需显式恢复备份后才能继续保存。"), diagnostics);
                return new(backup.Value, false, true, diagnostics);
            }
            if (backup.Error is not null) Emit(backup.Error, diagnostics);
            return new(defaults(), primary.Missing && backup.Missing, false, diagnostics);
        }
        catch (Exception error) when (IsStorageError(error))
        {
            Emit(new(DiagnosticLevel.Error, "storage.transaction.failed",
                $"无法协调读取：{file}。仅以内存默认值运行，保存已禁用。", error), diagnostics);
            return new(defaults(), false, false, diagnostics);
        }
    }

    public void Save(T value)
    {
        try
        {
            using var transaction = StorageTransaction.Acquire(file, transactionTimeout, report);
            validate(value);
            var primary = Read(file);
            if (!primary.Missing && primary.Value is null)
                throw new PersistenceException($"拒绝覆盖不可读、损坏或不支持版本的文件：{file}", primary.Error?.Exception);
            var backup = Read(BackupFile);
            if (primary.Missing && !backup.Missing)
                throw new PersistenceException($"主文件缺失但备份存在；请先显式恢复备份：{BackupFile}");
            if (backup.FutureSchema)
                throw new PersistenceException($"拒绝覆盖高版本备份：{BackupFile}");
            if (!backup.Missing && backup.Value is null)
                throw new PersistenceException($"拒绝覆盖不可读或损坏的备份：{BackupFile}。请先归档并处理备份。", backup.Error?.Exception);
            WriteAtomic(value, BackupFile);
        }
        catch (Exception error) when (IsStorageError(error))
        {
            report(new(DiagnosticLevel.Error, "storage.save.failed", $"保存失败：{file}。不会回退到其他目录。", error));
            throw new PersistenceException($"保存失败：{file}。{error.Message}", error);
        }
    }

    public void RestoreBackup()
    {
        try
        {
            using var transaction = StorageTransaction.Acquire(file, transactionTimeout, report);
            var primary = Read(file);
            if (primary.FutureSchema)
                throw new PersistenceException($"拒绝将高版本文件降级：{file}");
            var backup = Read(BackupFile);
            if (backup.Value is null)
                throw new PersistenceException($"没有可用的有效备份：{BackupFile}", backup.Error?.Exception);

            // Preserve the exact original bytes before replacement. The normal .bak stays intact.
            if (!primary.Missing)
            {
                var preserved = file + $".preserved-{Guid.NewGuid():N}";
                using (var original = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var copy = new FileStream(preserved, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    original.CopyTo(copy);
                    copy.Flush(flushToDisk: true);
                }
                report(new(DiagnosticLevel.Warning, "storage.original.preserved", $"原文件已保留：{preserved}"));
            }
            WriteAtomic(backup.Value, backupFile: null);
            report(new(DiagnosticLevel.Information, "storage.backup.restored", $"备份已恢复：{file}"));
        }
        catch (Exception error) when (IsStorageError(error))
        {
            report(new(DiagnosticLevel.Error, "storage.recovery.failed", $"备份恢复失败：{file}", error));
            throw new PersistenceException($"备份恢复失败：{file}。原文件和备份不应手动覆盖。", error);
        }
    }

    private void WriteAtomic(T value, string? backupFile)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        if (bytes.Length > MaximumDocumentBytes) throw new PersistenceException("JSON 文件超出大小限制。");
        var temporary = file + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            committer.Commit(temporary, file, backupFile);
            // A committer returning is not proof of success. Verify the committed model while still owning the transaction.
            var committed = Read(file);
            if (committed.Value is null || !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(committed.Value, options)))
                throw new PersistenceException("提交后读取校验未确认目标快照；保存结果不能报告为成功。", committed.Error?.Exception);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                report(new(DiagnosticLevel.Warning, "storage.temporary.cleanup.failed",
                    $"临时文件未能清理：{temporary}。启动时不会将临时文件当作有效数据。", error));
            }
        }
    }

    private ReadAttempt Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumDocumentBytes) throw new InvalidDataException("JSON 文件超出 64 KiB 限制。");
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var schema) ||
                schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version))
                throw new InvalidDataException("缺少有效的 schemaVersion。");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name)) throw new InvalidDataException("JSON 包含重复字段。");
            if (version > 1)
                return new(default, false, true, new(DiagnosticLevel.Error, "storage.schema.future",
                    $"文件使用不支持的高版本 schemaVersion={version}：{path}。不会降级或覆盖。"));
            if (version != 1) throw new InvalidDataException("不支持的 schemaVersion。");
            var value = root.Deserialize<T>(options) ?? throw new InvalidDataException("JSON 内容为空。");
            validate(value);
            return new(value, false, false, null);
        }
        catch (FileNotFoundException) { return new(default, true, false, null); }
        catch (DirectoryNotFoundException) { return new(default, true, false, null); }
        catch (Exception error) when (IsStorageError(error))
        {
            return new(default, false, false, new(DiagnosticLevel.Error, "storage.read.failed",
                $"文件不可读、损坏或包含无效参数：{path}。原文件保持不变。", error));
        }
    }

    private void Emit(PersistenceDiagnostic diagnostic, List<PersistenceDiagnostic> diagnostics)
    {
        diagnostics.Add(diagnostic);
        report(diagnostic);
    }

    private static bool IsStorageError(Exception error) => error is IOException or InvalidDataException or
        UnauthorizedAccessException or JsonException or ArgumentException or PersistenceException or
        NotSupportedException or System.Security.SecurityException;

    private sealed record ReadAttempt(T? Value, bool Missing, bool FutureSchema, PersistenceDiagnostic? Error);
}
