using System;
using System.Collections.Generic;
using System.Threading;
using PomodoroTimer.Spike.Infrastructure;

namespace PomodoroTimer.Spike.Services;

public enum CheckpointStatus { Saved, Failed, Disabled, NotRequired, InProgress }
public sealed record CheckpointResult(CheckpointStatus Status, Exception? Error = null);
public sealed record ExitCleanupStep(string Name, Action Run);

// WPF calls this synchronously on its dispatcher. Duplicate/reentrant exit requests do not execute any steps again.
public sealed class ExitCoordinator(Action<PersistenceDiagnostic> report)
{
    private readonly Action<PersistenceDiagnostic> report = DiagnosticReporter.Safe(report);
    private int started;
    private CheckpointResult? finalCheckpoint;
    public CheckpointResult? FinalCheckpoint => Volatile.Read(ref finalCheckpoint);
    public bool Completed { get; private set; }

    public void Run(Func<CheckpointResult> finalize, IReadOnlyList<ExitCleanupStep> cleanup,
        Action releaseOwnership, Action raiseExit)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) return;
        CheckpointResult result;
        try { result = finalize(); }
        catch (Exception error) { result = new(CheckpointStatus.Failed, error); }
        Volatile.Write(ref finalCheckpoint, result);
        // Never transfer ownership if a caller hands us an unfinished checkpoint.
        if (result.Status == CheckpointStatus.InProgress)
        {
            report(new(DiagnosticLevel.Error, "exit.checkpoint.incomplete", "最终保存尚未结束；本次退出协调不会释放所有权。"));
            // No step has run. A later, finished dispatcher checkpoint may retry; never wait on ourselves.
            Volatile.Write(ref started, 0);
            return;
        }
        report(new(result.Status is CheckpointStatus.Failed or CheckpointStatus.Disabled ? DiagnosticLevel.Error : DiagnosticLevel.Information,
            "exit.checkpoint." + result.Status.ToString().ToLowerInvariant(),
            $"最终保存结果：{result.Status}。Saved 表示写入、Flush、提交及读取校验完成，不代表硬件断电保证。", result.Error));
        foreach (var step in cleanup) Step(step.Name, step.Run);
        Step("ownership.release", releaseOwnership);
        Step("wpf.exit", raiseExit);
        Completed = true;
    }

    private void Step(string name, Action action)
    {
        try
        {
            action();
            report(new(DiagnosticLevel.Information, "exit.step.completed", $"退出步骤完成：{name}"));
        }
        catch (Exception error)
        {
            report(new(DiagnosticLevel.Error, "exit.step.failed", $"退出步骤失败：{name}。仍继续后续清理。", error));
        }
    }
}
