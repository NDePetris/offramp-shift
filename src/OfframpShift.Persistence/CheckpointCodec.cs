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
            if (version is not (1 or 2 or 3 or MonitoringCheckpoint.CurrentVersion)) throw new CheckpointStorageException(StorageFailure.UnsupportedVersion);
            var root = Object(document.RootElement, "Version", "SavedUtc", "Configuration", "State");
            string[] configurationFields = ["AllowanceMs", "RequiredBreakMs", "IdleThresholdMs", "GraceMs",
                "RepeatNudgeMs", "SnoozeMs", "MaximumEvidenceAgeMs", "Mode", "Applications"];
            string[] stateFields = ["UsageMs", "Exhausted", "BreakProgressMs", "BreakCompleted", "IdleProgressMs",
                "Paused", "Intervention", "ReminderRemainingMs", "SnoozeRemainingMs", "EffectSequence", "Recovery"];
            var c = Object(root["Configuration"], version == 1 ? configurationFields : version == 2 ?
                [.. configurationFields, "Accounting", "EstimationPolicyVersion"] :
                [.. configurationFields, "Accounting", "EstimationPolicyVersion", "InteractiveRepeatNudgeMs"]);
            string[] estimatedFields = [.. stateFields, "EstimatedUsageMs", "BreakUncertaintyMs", "ConsecutiveUncertaintyMs"];
            var s = Object(root["State"], version == 1 ? stateFields : version < 4 ? estimatedFields :
                [.. estimatedFields, "PauseUntilUtc"]);
            if (version >= 2 && c["EstimationPolicyVersion"].GetInt32() > EstimationPolicy.Version)
                throw new CheckpointStorageException(StorageFailure.UnsupportedVersion);
            var configuration = new MonitoringConfiguration
            {
                AllowanceMs = c["AllowanceMs"].GetInt64(),
                RequiredBreakMs = c["RequiredBreakMs"].GetInt64(),
                IdleThresholdMs = c["IdleThresholdMs"].GetInt64(),
                GraceMs = c["GraceMs"].GetInt64(),
                RepeatNudgeMs = c["RepeatNudgeMs"].GetInt64(),
                SnoozeMs = c["SnoozeMs"].GetInt64(),
                InteractiveRepeatNudgeMs = version >= 3 ? c["InteractiveRepeatNudgeMs"].GetInt64() : 60_000,
                MaximumEvidenceAgeMs = c["MaximumEvidenceAgeMs"].GetInt64(),
                Accounting = version == 1 ? AccountingMode.CertifiedOnly : (AccountingMode)c["Accounting"].GetInt32(),
                EstimationPolicyVersion = version == 1 ? EstimationPolicy.Version : c["EstimationPolicyVersion"].GetInt32(),
                Mode = (ClassificationMode)c["Mode"].GetInt32(),
                Applications = c["Applications"].EnumerateArray().Select(x => x.GetString() ?? throw Invalid()).ToImmutableArray()
            };
            var state = new EngineState(s["UsageMs"].GetInt64(), s["Exhausted"].GetBoolean(), s["BreakProgressMs"].GetInt64(),
                s["BreakCompleted"].GetBoolean(), s["IdleProgressMs"].GetInt64(), s["Paused"].GetBoolean(),
                (InterventionState)s["Intervention"].GetInt32(), s["ReminderRemainingMs"].GetInt64(),
                s["SnoozeRemainingMs"].GetInt64(), s["EffectSequence"].GetInt64(), (RecoveryStatus)s["Recovery"].GetInt32())
            {
                PauseUntilUtc = version < 4 || s["PauseUntilUtc"].ValueKind == JsonValueKind.Null ? null : s["PauseUntilUtc"].GetDateTimeOffset(),
                EstimatedUsageMs = version == 1 ? 0 : s["EstimatedUsageMs"].GetInt64(),
                BreakUncertaintyMs = version == 1 ? 0 : s["BreakUncertaintyMs"].GetInt64(),
                ConsecutiveUncertaintyMs = version == 1 ? 0 : s["ConsecutiveUncertaintyMs"].GetInt64()
            };
            var checkpoint = new MonitoringCheckpoint(version, root["SavedUtc"].GetDateTimeOffset(), configuration, state);
            ValidateReadable(checkpoint, now);
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

    internal static void Validate(MonitoringCheckpoint checkpoint, DateTimeOffset now)
    {
        if (checkpoint.Version != MonitoringCheckpoint.CurrentVersion) throw new CheckpointStorageException(StorageFailure.UnsupportedVersion);
        if (checkpoint.Configuration?.EstimationPolicyVersion > EstimationPolicy.Version)
            throw new CheckpointStorageException(StorageFailure.UnsupportedVersion);
        ValidateReadable(checkpoint, now);
    }

    private static void ValidateReadable(MonitoringCheckpoint checkpoint, DateTimeOffset now)
    {
        // Check structure/domain consistency first. A future timestamp alone may
        // be a backward host clock adjustment; it still cannot be restored now.
        if (checkpoint.SavedUtc == default || checkpoint.SavedUtc.Offset != TimeSpan.Zero ||
            !checkpoint.IsValid(checkpoint.SavedUtc)) throw Invalid();
        if (!checkpoint.IsValid(now)) throw new CheckpointStorageException(StorageFailure.ClockUnavailable);
    }

    private static CheckpointStorageException Invalid() => new(StorageFailure.InvalidPayload);
}
