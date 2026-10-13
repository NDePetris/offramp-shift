using System.Collections.Immutable;
using System.Text.Json;
using OfframpShift.Core;

namespace OfframpShift.Persistence;

public static class CheckpointCodec
{
    public static string Encode(MonitoringCheckpoint checkpoint, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Validate(checkpoint, now);
        return JsonSerializer.Serialize(checkpoint);
    }

    public static MonitoringCheckpoint Decode(string payload, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var versions = document.RootElement.EnumerateObject().Where(x => x.Name == "Version").ToArray();
            if (versions.Length != 1) throw Invalid();
            var version = versions[0].Value.GetInt32();
            if (version != MonitoringCheckpoint.CurrentVersion) throw new CheckpointStorageException(StorageFailure.UnsupportedVersion);
            var root = Object(document.RootElement, "Version", "SavedUtc", "Configuration", "State");
            var c = Object(root["Configuration"], "AllowanceMs", "RequiredBreakMs", "IdleThresholdMs", "GraceMs",
                "RepeatNudgeMs", "SnoozeMs", "MaximumEvidenceAgeMs", "Mode", "Applications");
            var s = Object(root["State"], "UsageMs", "Exhausted", "BreakProgressMs", "BreakCompleted", "IdleProgressMs",
                "Paused", "Intervention", "ReminderRemainingMs", "SnoozeRemainingMs", "EffectSequence", "Recovery");
            var configuration = new MonitoringConfiguration
            {
                AllowanceMs = c["AllowanceMs"].GetInt64(),
                RequiredBreakMs = c["RequiredBreakMs"].GetInt64(),
                IdleThresholdMs = c["IdleThresholdMs"].GetInt64(),
                GraceMs = c["GraceMs"].GetInt64(),
                RepeatNudgeMs = c["RepeatNudgeMs"].GetInt64(),
                SnoozeMs = c["SnoozeMs"].GetInt64(),
                MaximumEvidenceAgeMs = c["MaximumEvidenceAgeMs"].GetInt64(),
                Mode = (ClassificationMode)c["Mode"].GetInt32(),
                Applications = c["Applications"].EnumerateArray().Select(x => x.GetString() ?? throw Invalid()).ToImmutableArray()
            };
            var state = new EngineState(s["UsageMs"].GetInt64(), s["Exhausted"].GetBoolean(), s["BreakProgressMs"].GetInt64(),
                s["BreakCompleted"].GetBoolean(), s["IdleProgressMs"].GetInt64(), s["Paused"].GetBoolean(),
                (InterventionState)s["Intervention"].GetInt32(), s["ReminderRemainingMs"].GetInt64(),
                s["SnoozeRemainingMs"].GetInt64(), s["EffectSequence"].GetInt64(), (RecoveryStatus)s["Recovery"].GetInt32());
            var checkpoint = new MonitoringCheckpoint(version, root["SavedUtc"].GetDateTimeOffset(), configuration, state);
            Validate(checkpoint, now);
            return checkpoint;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        {
            throw Invalid();
        }
    }

    private static Dictionary<string, JsonElement> Object(JsonElement element, params string[] fields)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !values.TryAdd(property.Name, property.Value)) throw Invalid();
        if (values.Count != fields.Length) throw Invalid();
        return values;
    }

    private static void Validate(MonitoringCheckpoint checkpoint, DateTimeOffset now)
    {
        if (checkpoint.Version != MonitoringCheckpoint.CurrentVersion) throw new CheckpointStorageException(StorageFailure.UnsupportedVersion);
        if (!checkpoint.IsValid(now) || checkpoint.SavedUtc.Offset != TimeSpan.Zero) throw Invalid();
    }

    private static CheckpointStorageException Invalid() => new(StorageFailure.InvalidPayload);
}
