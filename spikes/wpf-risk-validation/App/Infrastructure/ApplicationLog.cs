// Source compatibility only: notification, tray and startup code are outside this phase's scope.
global using SpikeLog = PomodoroTimer.Spike.Infrastructure.ApplicationLog;

using System;

namespace PomodoroTimer.Spike.Infrastructure;

public static class ApplicationLog
{
    private static readonly LogService Service = new(PortablePaths.Default);
    public static string DirectoryPath => PortablePaths.Default.LogsDirectory;
    public static PersistenceDiagnostic? LastFailure => Service.LastFailure;
    public static event Action<PersistenceDiagnostic>? Failed
    {
        add => Service.Failed += value;
        remove => Service.Failed -= value;
    }

    public static void Write(string message) => Service.Write(DiagnosticLevel.Information, "application.event", message);
    public static void Write(PersistenceDiagnostic diagnostic) =>
        Service.Write(diagnostic.Level, diagnostic.Event, diagnostic.Message, diagnostic.Exception);
}
