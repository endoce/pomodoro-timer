using System.Text.Json.Serialization;

namespace PomodoroTimer.Spike.Core.Models;

// There is only one fixed-duration timer in this PoC, not work/break modes or cycle history.
public sealed record TimerSnapshot
{
    public const int CurrentSchemaVersion = 1;
    [JsonRequired] public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    [JsonRequired] public TimerState State { get; init; }
    [JsonRequired] public long DurationTicks { get; init; }
    [JsonRequired] public long RemainingTicks { get; init; }
}
