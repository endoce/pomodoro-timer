using System;
using System.IO;

namespace PomodoroTimer.Spike.Infrastructure;

public static class SpikeLog
{
    private static readonly object Gate = new();
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PomodoroTimerSpike", "logs");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(Path.Combine(DirectoryPath, $"spike-{DateTime.UtcNow:yyyy-MM-dd}.log"),
                    $"{DateTimeOffset.UtcNow:O} PID={Environment.ProcessId} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
