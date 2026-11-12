using System;
using System.Diagnostics;

namespace PomodoroTimer.Spike.Infrastructure;

public enum DiagnosticLevel { Information, Warning, Error }

public sealed record PersistenceDiagnostic(
    DiagnosticLevel Level, string Event, string Message, Exception? Exception = null);

public sealed class PersistenceException(string message, Exception? inner = null)
    : Exception(message, inner);

internal static class DiagnosticReporter
{
    // A UI/log sink must not mask the original storage error or break timer events.
    public static Action<PersistenceDiagnostic> Safe(Action<PersistenceDiagnostic> report) => diagnostic =>
    {
        try { report(diagnostic); }
        catch (Exception error)
        {
            Fallback(diagnostic.Event, error.GetType().Name);
        }
    };

    public static void Fallback(string eventName, string errorType)
    {
        // No arbitrary exception text here: even fallback diagnostics must not leak credentials.
        var text = $"{DateTimeOffset.UtcNow:O} Warning {eventName}: {errorType}; diagnostic sink unavailable.";
        try { Trace.WriteLine(text); Console.Error.WriteLine(text); }
        catch (Exception) { /* Last-resort output itself is unavailable; never crash the timer. */ }
    }
}
