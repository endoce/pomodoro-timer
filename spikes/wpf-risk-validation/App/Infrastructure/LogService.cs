using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PomodoroTimer.Spike.Infrastructure;

public sealed record LogLimits
{
    public long MaximumFileBytes { get; init; } = 5 * 1024 * 1024;
    public long MaximumTotalBytes { get; init; } = 50 * 1024 * 1024;
    public int RetentionDays { get; init; } = 14;
}

// Each write owns a short-lived handle without FileShare.Delete. Cleanup never targets that file.
public sealed class LogService
{
    private readonly object gate = new();
    private readonly PortablePaths paths;
    private readonly LogLimits limits;
    private readonly Func<DateTimeOffset> utcNow;
    private static readonly Regex FileName = new(
        @"^pomodoro-(\d{4}-\d{2}-\d{2})-(\d{4,})\.log$", RegexOptions.CultureInvariant);
    private static readonly Regex SecretFields = new(
        """(?i)["']?(?:api[_-]?key|password|passwd|secret|access[_-]?token|refresh[_-]?token)["']?\s*[:=]\s*""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Authorization = new(
        @"(?i)(\b(?:Bearer|Basic)\s+)[A-Za-z0-9._~+/=-]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    public LogService(PortablePaths paths, LogLimits? limits = null, Func<DateTimeOffset>? utcNow = null)
    {
        this.paths = paths;
        this.limits = limits ?? new();
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        if (this.limits.MaximumFileBytes < 512 || this.limits.MaximumFileBytes > 5 * 1024 * 1024 ||
            this.limits.MaximumTotalBytes < this.limits.MaximumFileBytes ||
            this.limits.MaximumTotalBytes > 50 * 1024 * 1024 ||
            this.limits.RetentionDays is < 1 or > 14)
            throw new ArgumentOutOfRangeException(nameof(limits));
    }

    public PersistenceDiagnostic? LastFailure { get; private set; }
    public event Action<PersistenceDiagnostic>? Failed;

    public void Write(DiagnosticLevel level, string eventName, string message, Exception? exception = null)
    {
        lock (gate)
        {
            try
            {
                var now = utcNow().ToUniversalTime();
                var day = DateOnly.FromDateTime(now.UtcDateTime);
                var bytes = Entry(now, level, eventName, message, exception);
                Directory.CreateDirectory(paths.LogsDirectory);
                var files = Files();
                var sequence = files.Where(f => f.Day == day).Select(f => f.Sequence).DefaultIfEmpty(0).Max();
                var current = paths.LogFile(day, sequence);
                if (File.Exists(current) && new FileInfo(current).Length + bytes.Length > limits.MaximumFileBytes)
                    current = paths.LogFile(day, checked(sequence + 1));

                // Make space before writing; locked files are retained with an explicit warning.
                Cleanup(day, current, bytes.Length);
                using (var stream = new FileStream(current, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                {
                    if (stream.Length + bytes.Length > limits.MaximumFileBytes)
                        throw new IOException("日志文件大小在写入前发生变化；为保护容量限制，本条未写入。");
                    stream.Seek(0, SeekOrigin.End);
                    stream.Write(bytes);
                    stream.Flush();
                }
                Cleanup(day, current, incomingBytes: 0);
            }
            catch (Exception error)
            {
                Failure("logs.write.failed", "日志写入失败；计时器仍可运行。请检查 Logs 目录权限及可用空间。", error);
            }
        }
    }

    private byte[] Entry(DateTimeOffset time, DiagnosticLevel level, string eventName, string message, Exception? exception)
    {
        var safeEvent = Redact(eventName);
        var safeMessage = Redact(message);
        var safeException = exception is null ? null : Redact(exception.ToString());
        byte[] Serialize() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            timestampUtc = time.ToString("O", CultureInfo.InvariantCulture),
            level = level.ToString(),
            @event = safeEvent,
            message = safeMessage,
            exception = safeException
        }) + "\n");
        var bytes = Serialize();
        // Preserve a valid JSON entry under the byte cap, including multibyte Chinese text.
        if (bytes.Length > limits.MaximumFileBytes)
        {
            safeEvent = "logs.entry.truncated";
            safeMessage = "日志条目超过单文件上限，内容已省略。";
            safeException = exception?.GetType().Name;
            bytes = Serialize();
            if (bytes.Length > limits.MaximumFileBytes)
            {
                safeException = "Exception details omitted";
                bytes = Serialize();
            }
        }
        return bytes;
    }

    private static string Redact(string text)
    {
        var result = new StringBuilder(text.Length);
        var consumed = 0;
        foreach (Match field in SecretFields.Matches(text))
        {
            if (field.Index < consumed) continue;
            result.Append(text, consumed, field.Index - consumed);
            result.Append(field.Value).Append("[REDACTED]");
            consumed = CredentialEnd(text, field.Index + field.Length);
        }
        result.Append(text, consumed, text.Length - consumed);
        return Authorization.Replace(result.ToString(), "$1[REDACTED]");
    }

    private static int CredentialEnd(string text, int start)
    {
        if (start == text.Length) return start;
        var quote = text[start];
        if (quote is '\'' or '"')
        {
            // Each escape consumes its following character. Even/odd backslash runs therefore remain unambiguous.
            for (var i = start + 1; i < text.Length; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] != quote) continue;
                var end = i + 1;
                // A malformed immediate suffix is ambiguous: omit the rest rather than partially expose a credential.
                return end == text.Length || Boundary(text[end]) ? end : text.Length;
            }
            return text.Length; // Unterminated quoted value, including a dangling backslash: fail closed.
        }
        var position = start;
        while (position < text.Length && !Boundary(text[position])) position++;
        return position;
    }

    private static bool Boundary(char value) => char.IsWhiteSpace(value) || value is ',' or ';' or '}' or ']';

    private List<LogFile> Files()
    {
        var result = new List<LogFile>();
        foreach (var path in Directory.EnumerateFiles(paths.LogsDirectory, "pomodoro-*.log"))
        {
            var match = FileName.Match(Path.GetFileName(path));
            if (match.Success && DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) &&
                int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
                result.Add(new(path, day, sequence, new FileInfo(path).Length));
        }
        return result;
    }

    private void Cleanup(DateOnly today, string activeFile, long incomingBytes)
    {
        var files = Files().OrderBy(f => f.Day).ThenBy(f => f.Sequence).ToList();
        var total = files.Sum(f => f.Bytes) + incomingBytes;
        foreach (var file in files)
        {
            if (file.Path == activeFile) continue;
            if (file.Day >= today.AddDays(1 - limits.RetentionDays) && total <= limits.MaximumTotalBytes) continue;
            try
            {
                // Verify exclusive access before deleting. An in-use file is skipped, never truncated.
                using var exclusive = new FileStream(file.Path, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.DeleteOnClose);
                total -= file.Bytes;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Failure("logs.cleanup.failed", "旧日志正在使用或无法清理，已保留；容量/保留天数限制可能暂时超出。", error);
            }
        }
        if (total > limits.MaximumTotalBytes)
            Failure("logs.capacity.exceeded", "无法在不影响正在使用文件的前提下满足日志总量限制。", null);
    }

    private void Failure(string eventName, string message, Exception? error)
    {
        LastFailure = new(DiagnosticLevel.Warning, eventName, message, error);
        DiagnosticReporter.Fallback(eventName, error?.GetType().Name ?? "CapacityLimit");
        var handlers = Failed;
        if (handlers is null) return;
        foreach (Action<PersistenceDiagnostic> handler in handlers.GetInvocationList())
            DiagnosticReporter.Safe(handler)(LastFailure);
    }

    private sealed record LogFile(string Path, DateOnly Day, int Sequence, long Bytes);
}
