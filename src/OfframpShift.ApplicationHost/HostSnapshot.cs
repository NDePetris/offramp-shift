using OfframpShift.Core;
using OfframpShift.Persistence;

namespace OfframpShift.ApplicationHost;

public sealed record HostSnapshot(long Sequence, long PublishedTick, PersistenceStatus Persistence,
    StorageFailure? StorageFailure, SettingsStatus SettingsStatus, WindowsSettings Settings,
    EngineState? State, MonitoringConfiguration? Configuration, DesktopScope Context,
    string Status, string Detail, bool Available, bool BindingsValid, long RetryInMs,
    bool ObservationAllowed = false, bool CanConsent = false, bool CanAssign = false,
    AssignmentStatus PersonalAssignment = AssignmentStatus.Unavailable,
    AssignmentStatus WorkAssignment = AssignmentStatus.Unavailable, bool ConsentNeedsReview = false,
    bool CanKeepNudgeOpen = false, long ReminderGeneration = 0, Guid ReminderEpoch = default,
    string ReminderStatus = "No committed nudge event yet.", Guid PresentationId = default)
{
    // Process-local presentation boundary; never persisted or used for eligibility.
    public Guid AccountingPresentationEpoch { get; init; }
    public string SwitchingUnavailableReason { get; init; } = "Switching availability is unknown.";
    public string PauseRecoveryDetail { get; init; } = "";
}

public enum AssignmentStatus { Unassigned, Valid, Unavailable, Missing, Invalid }

public sealed record HostCommandResult(bool Succeeded, string Message = "")
{
    public bool ConfigurationConflict { get; init; }
    public NudgePresentation? Presentation { get; init; }
    public DesktopFeasibility.SwitchAuthorization? Switching { get; init; }
    public static HostCommandResult Success(string message = "") => new(true, message);
    public static HostCommandResult Failure(string message) => new(false, message);
}

public enum HostCommandKind
{
    Consent, Start, Pause, Resume, RetryMonitoring, RetrySave, ReopenStorage,
    AssignPersonal, AssignWork, ChangeDurations, ChangeAppearance, ChangeCloseBehavior, Recover, RepairSettings,
    ChangeBreakReminders, ResetUsage, ChangeInteractiveRepeat, DeferInteractive, BeginNudgePresentation, CompleteNudgePresentation,
    ChangeSwitchingConsent, AuthorizeSwitch
}

public sealed record HostCommand(HostCommandKind Kind)
{
    public DateTimeOffset? PauseUntilUtc { get; init; }
    public ExpectedSettingsValues? Expected { get; init; }
    public bool Confirmed { get; init; }
    public long AllowanceMs { get; init; }
    public long RequiredBreakMs { get; init; }
    public long IdleThresholdMs { get; init; }
    public AppTheme Theme { get; init; }
    public bool? CloseToTray { get; init; }
    public ReminderStyle ReminderStyle { get; init; }
    public long DurationMs { get; init; }
    public ReminderEvent? Reminder { get; init; }
    public nint NudgeWindow { get; init; }
    public NudgePresentation? Presentation { get; init; }
}

// Owner-thread preconditions compare only the fields owned by the command.
// Optional for existing immediate commands (including first-close preference).
public sealed record ExpectedSettingsValues(long AllowanceMs, long RequiredBreakMs, long IdleThresholdMs,
    long RepeatMs, AppTheme Theme, bool? CloseToTray, ReminderStyle ReminderStyle, bool SwitchingConsent,
    string SwitchingBuild, string SwitchingFamily)
{
    public static ExpectedSettingsValues From(WindowsSettings settings, MonitoringConfiguration? configuration) => new(
        configuration?.AllowanceMs ?? 0, configuration?.RequiredBreakMs ?? 0, configuration?.IdleThresholdMs ?? 0,
        configuration?.InteractiveRepeatNudgeMs ?? 0, settings.Theme, settings.CloseToTray, settings.ReminderStyle,
        settings.SwitchingConsent, settings.SwitchingConsentBuild, settings.SwitchingConsentFamily);

    public bool Matches(HostCommandKind kind, WindowsSettings settings, MonitoringConfiguration configuration) => kind switch
    {
        HostCommandKind.ChangeDurations => AllowanceMs == configuration.AllowanceMs && RequiredBreakMs == configuration.RequiredBreakMs && IdleThresholdMs == configuration.IdleThresholdMs,
        HostCommandKind.ChangeInteractiveRepeat => RepeatMs == configuration.InteractiveRepeatNudgeMs,
        HostCommandKind.ChangeAppearance => Theme == settings.Theme && CloseToTray == settings.CloseToTray,
        HostCommandKind.ChangeBreakReminders => ReminderStyle == settings.ReminderStyle,
        HostCommandKind.ChangeSwitchingConsent => SwitchingConsent == settings.SwitchingConsent && SwitchingBuild == settings.SwitchingConsentBuild && SwitchingFamily == settings.SwitchingConsentFamily,
        _ => true
    };
}

// Process-local, single-use presentation transaction; never persisted or renewed.
public sealed record NudgePresentation(Guid Id, long Sequence, nint OriginalForeground,
    Guid PersonalDesktop, Guid Epoch, long ExpiresTick);
