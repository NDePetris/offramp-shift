using OfframpShift.Core;
using OfframpShift.Persistence;

namespace OfframpShift.ApplicationHost;

public sealed record HostSnapshot(long Sequence, long PublishedTick, PersistenceStatus Persistence,
    StorageFailure? StorageFailure, SettingsStatus SettingsStatus, WindowsSettings Settings,
    EngineState? State, MonitoringConfiguration? Configuration, DesktopScope Context,
    string Status, string Detail, bool Available, bool BindingsValid, long RetryInMs,
    bool ObservationAllowed = false);

public enum HostCommandKind
{
    Consent, Start, Pause, Resume, RetryMonitoring, RetrySave, ReopenStorage,
    AssignPersonal, AssignWork, ChangeDurations, ChangeAppearance, Recover, RepairSettings
}

public sealed record HostCommand(HostCommandKind Kind)
{
    public bool Confirmed { get; init; }
    public long AllowanceMs { get; init; }
    public long RequiredBreakMs { get; init; }
    public long IdleThresholdMs { get; init; }
    public AppTheme Theme { get; init; }
    public bool? CloseToTray { get; init; }
}
