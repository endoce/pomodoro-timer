using System.Text.Json.Serialization;

namespace PomodoroTimer.Spike.Core.Models;

// The current UI has no editable settings. Do not invent durations, themes or notification toggles.
public sealed record ApplicationSettings
{
    public const int CurrentSchemaVersion = 1;
    [JsonRequired] public int SchemaVersion { get; init; } = CurrentSchemaVersion;
}
