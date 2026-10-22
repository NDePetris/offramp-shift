using System.ComponentModel;
using System.Globalization;
using OfframpShift.ApplicationHost;
using OfframpShift.Persistence;
using OfframpShift.Core;

namespace OfframpShift.App;

public sealed class DashboardModel : INotifyPropertyChanged
{
    private readonly CommittedPresentation committed = new();
    private bool commandPending;
    private long lastNow;
    public string HeroLabel { get; private set; } = "Personal time remaining";
    public string HeroValue { get; private set; } = "—";
    public string Arithmetic { get; private set; } = "Recorded usage unavailable";
    public string RecordedQualifier { get; private set; } = "";
    public string ContextLabel { get; private set; } = "Unknown";
    public string ReminderLabel { get; private set; } = "";
    public string ReminderValue { get; private set; } = "";
    public bool ShowReminder => ReminderLabel.Length > 0;
    public bool ShowUsageProgress { get; private set; }
    public string BreakValue { get; private set; } = "—";
    public string BreakExplanation { get; private set; } = "Recorded break progress unavailable";
    public string Health { get; private set; } = "Starting monitoring";
    public bool ShowHealthDetail { get; private set; } = true;
    public bool ShowExhaustionExplanation { get; private set; }
    public bool CommandsIdle => !commandPending;
    public bool ShowMonitoringAction { get; private set; }
    public string MonitoringActionLabel { get; private set; } = "Pause monitoring";
    public bool CanToggleMonitoring => CanPause || CanResume;
    internal RecordedAccounting? Recorded => committed.Recorded;
    public event PropertyChangedEventHandler? PropertyChanged;
    public HostSnapshot? Snapshot { get; private set; }
    internal TraySummary Tray { get; private set; } = TraySummary.Starting;
    public string Status { get; private set; } = "Starting";
    public string Detail { get; private set; } = "Opening local settings and accounting storage…";
    public string Context { get; private set; } = "Unknown";
    public string Usage { get; private set; } = "Saved usage unavailable";
    public string Break { get; private set; } = "Break progress unavailable";
    public string Persistence { get; private set; } = "Opening storage";
    public string Bindings { get; private set; } = "Assign Personal and Work in Settings";
    public string ConsentStatus { get; private set; } = "Experimental monitoring consent: Unavailable — starting";
    public string PersonalStatus { get; private set; } = "Personal desktop: Unavailable — starting";
    public string WorkStatus { get; private set; } = "Work desktop: Unavailable — starting";
    public string StartStatus { get; private set; } = "Start monitoring: Incomplete — finish setup first";
    public string NextSetupAction { get; private set; } = "Wait for local settings to open.";
    public bool CanConsent { get; private set; }
    public bool CanAssign { get; private set; }
    public bool NeedsSetup { get; private set; } = true;
    public string Confidence { get; private set; } = "Estimated monitoring • owner evaluation";
    public string Advisory { get; private set; } = "";
    public double UsageProgress { get; private set; }
    public double BreakProgress { get; private set; }
    public bool CanStart { get; private set; }
    public bool CanPause { get; private set; }
    public bool CanResume { get; private set; }
    public bool CanRetrySave { get; private set; }
    public bool CanRetryMonitoring { get; private set; }
    public bool CanRecover { get; private set; }
    public bool CanReopen { get; private set; }
    public bool CanRepairSettings { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanResetUsage { get; private set; }
    public bool CanEditReminders { get; private set; }
    public string RetryLabel { get; private set; } = "Retry monitoring";

    internal void MarkUnavailable(string detail)
    {
        committed.Terminate();
        HeroValue = "—"; Arithmetic = "Recorded usage unavailable"; RecordedQualifier = "";
        BreakValue = "—"; BreakExplanation = "Recorded break progress unavailable";
        Usage = "Saved usage unavailable"; Break = "Saved break progress unavailable";
        UsageProgress = BreakProgress = 0; ContextLabel = "Unknown";
        ReminderLabel = ReminderValue = ""; ShowExhaustionExplanation = false;
        ShowUsageProgress = false;
        Health = "Monitoring owner stopped"; ShowHealthDetail = true;
        Status = "Unavailable"; Detail = detail; Context = "Unknown";
        Tray = TraySummary.Starting.Unavailable();
        CanStart = CanPause = CanResume = CanRetrySave = CanRetryMonitoring = CanRecover = CanReopen = CanRepairSettings = CanEdit = false;
        CanConsent = CanAssign = false;
        CanResetUsage = CanEditReminders = false;
        NeedsSetup = true;
        ConsentStatus = "Experimental monitoring consent: Unavailable";
        PersonalStatus = AssignmentText("Personal", Snapshot?.Settings.Personal, AssignmentStatus.Unavailable);
        WorkStatus = AssignmentText("Work", Snapshot?.Settings.Work, AssignmentStatus.Unavailable);
        Bindings = PersonalStatus + "\n" + WorkStatus;
        StartStatus = "Start monitoring: Unavailable";
        NextSetupAction = detail;
        Snapshot = null;
        PropertyChanged?.Invoke(this, new(null));
    }

    public void Apply(HostSnapshot snapshot, long nowTick)
    {
        if (!committed.Accept(snapshot, nowTick)) return;
        lastNow = nowTick;
        Snapshot = snapshot;
        var stale = !CommittedPresentation.IsFresh(snapshot, nowTick);
        Status = stale ? "Unavailable" : snapshot.Status;
        Detail = stale ? "Monitoring updates are stale. Displayed values are the last saved snapshot; no time is extrapolated." : snapshot.Detail;
        Context = stale ? "Unknown" : snapshot.Context.ToString();
        Persistence = snapshot.Persistence == PersistenceStatus.Ready ? "Accounting saved locally" : $"Accounting storage: {snapshot.Persistence} ({snapshot.StorageFailure})";
        if (committed.Recorded is { State: var state, Configuration: var configuration })
        {
            Usage = $"{Duration(state.UsageMs)} used of {CommittedPresentation.Duration(configuration.AllowanceMs, remaining: true)} allowance";
            Confidence = $"Estimated portion: {Duration(state.EstimatedUsageMs)}. Unknown time earns no usage or break credit.";
            Break = state.BreakCompleted ? "Break completed; allowance reset" :
                $"{Duration(state.BreakProgressMs)} of {CommittedPresentation.Duration(configuration.RequiredBreakMs, remaining: true)} qualifying break";
            Advisory = state.Exhausted ? snapshot.Available && !stale && !state.Paused && snapshot.Settings.MonitoringEnabled ?
                state.Intervention == OfframpShift.Core.InterventionState.PersonalDeferred ? "Extra Personal time active; usage continues." :
                state.Intervention == OfframpShift.Core.InterventionState.Snoozed ? "Reminders snoozed; usage continues." :
                state.Intervention == OfframpShift.Core.InterventionState.Nudging ? "Allowance exhausted — take a qualifying break." : "Allowance exhausted — grace period." :
                "Allowance exhausted. Advisory progress is suspended while monitoring is paused or unavailable." : "Allowance available";
            UsageProgress = Math.Clamp((double)state.UsageMs / configuration.AllowanceMs * 100, 0, 100);
            BreakProgress = Math.Clamp((double)state.BreakProgressMs / configuration.RequiredBreakMs * 100, 0, 100);
        }
        else { Usage = "Saved usage unavailable"; Break = "Saved break progress unavailable"; Advisory = "Advisory state unavailable"; UsageProgress = BreakProgress = 0; }
        CanEdit = !commandPending && !stale && snapshot.Persistence == PersistenceStatus.Ready;
        CanResetUsage = CanEdit;
        CanEditReminders = !commandPending && !stale && snapshot.SettingsStatus is SettingsStatus.Ready or SettingsStatus.Missing;
        CanStart = CanEdit && snapshot.ObservationAllowed && snapshot.CanAssign && snapshot.BindingsValid && snapshot.Settings.Complete &&
            (!snapshot.Settings.MonitoringEnabled || snapshot.Configuration?.Accounting != OfframpShift.Core.AccountingMode.BestEffort);
        CanPause = CanEdit && snapshot.State?.Paused == false && snapshot.Settings.MonitoringEnabled;
        CanResume = CanEdit && snapshot.State?.Paused == true && snapshot.Settings.MonitoringEnabled && snapshot.Available;
        CanRetrySave = !commandPending && !stale && snapshot.Persistence == PersistenceStatus.PendingCommit;
        CanRetryMonitoring = CanEdit && snapshot.ObservationAllowed && snapshot.RetryInMs == 0;
        CanRecover = !commandPending && !stale && snapshot.Persistence == PersistenceStatus.RecoveryRequired;
        CanReopen = !commandPending && !stale && snapshot.Persistence == PersistenceStatus.Blocked;
        CanRepairSettings = !commandPending && !stale && snapshot.SettingsStatus == SettingsStatus.Invalid;
        RetryLabel = snapshot.RetryInMs > 0 ? $"Retry monitoring ({(snapshot.RetryInMs + 999) / 1_000}s)" : "Retry monitoring";
        CanConsent = !commandPending && !stale && snapshot.CanConsent;
        CanAssign = !commandPending && !stale && snapshot.CanAssign;
        var consentComplete = snapshot.Settings.Consent && !snapshot.ConsentNeedsReview;
        ConsentStatus = "Experimental monitoring consent: " + (consentComplete ? "Completed — saved" :
            CanConsent ? snapshot.Settings.Consent ? "Incomplete — review for this Windows build" : "Incomplete — review and agree below" :
            "Unavailable — see monitoring status");
        PersonalStatus = AssignmentText("Personal", snapshot.Settings.Personal, stale ? AssignmentStatus.Unavailable : snapshot.PersonalAssignment);
        WorkStatus = AssignmentText("Work", snapshot.Settings.Work, stale ? AssignmentStatus.Unavailable : snapshot.WorkAssignment);
        Bindings = PersonalStatus + "\n" + WorkStatus;
        var started = snapshot.Settings.MonitoringEnabled && snapshot.Configuration?.Accounting == OfframpShift.Core.AccountingMode.BestEffort;
        NeedsSetup = stale || !started || !snapshot.BindingsValid || !snapshot.ObservationAllowed;
        StartStatus = "Start monitoring: " + (started ? "Completed — " + Status : CanStart ? "Ready — select Start monitoring" : "Unavailable — finish setup and resolve monitoring status");
        NextSetupAction = stale ? "Setup actions unavailable while updates are stale. Wait for fresh updates or restart the app." :
            CanConsent ? "Next: review experimental monitoring and agree in Settings." :
            !consentComplete ? "Setup unavailable. See monitoring status and storage recovery controls." :
            !snapshot.ObservationAllowed ? "Setup unavailable. See monitoring status and storage recovery controls." :
            !CanAssign ? "Desktop observation unavailable. Wait for Windows to settle, then use Retry monitoring if needed." :
            snapshot.PersonalAssignment != AssignmentStatus.Valid ? "Next: choose your Personal desktop in Task View, then use the tray menu to assign Personal." :
            snapshot.WorkAssignment != AssignmentStatus.Valid ? "Next: choose a different Work desktop in Task View, then use the tray menu to assign Work." :
            CanStart ? "Next: select Start monitoring." : started ? "Setup complete. Use Pause or Resume to control monitoring." :
            "Start monitoring unavailable. See monitoring status.";
        ProjectHome(snapshot, stale);
        Tray = TraySummary.Create(this);
        PropertyChanged?.Invoke(this, new(null));
    }
    internal void SetCommandPending(bool pending)
    {
        commandPending = pending;
        if (Snapshot is { } snapshot) Apply(snapshot, lastNow);
        else PropertyChanged?.Invoke(this, new(null));
    }
    private void ProjectHome(HostSnapshot snapshot, bool stale)
    {
        var currentContext = !stale && snapshot.Context != DesktopScope.Unknown;
        ContextLabel = currentContext ? snapshot.Context + " desktop" :
            committed.LastContext is DesktopScope.Personal or DesktopScope.Work ? "Last observed: " + committed.LastContext : "Unknown";
        Health = stale ? "Monitoring updates delayed" : snapshot.Persistence != PersistenceStatus.Ready ? "Accounting storage needs attention" :
            !snapshot.ObservationAllowed ? "Review monitoring setup" : !snapshot.Available ? "Observations unavailable" :
            snapshot.State?.Paused == true ? "Monitoring paused · Timers are not advancing" :
            !snapshot.Settings.MonitoringEnabled || snapshot.Configuration?.Accounting != AccountingMode.BestEffort ? "Monitoring is not started" : "Monitoring enabled · Observations current";
        ShowHealthDetail = stale || snapshot.Persistence != PersistenceStatus.Ready || !snapshot.Available || NeedsSetup;
        ShowMonitoringAction = snapshot.Settings.MonitoringEnabled;
        MonitoringActionLabel = snapshot.State?.Paused == true ? "Resume monitoring" : "Pause monitoring";
        var held = stale || !snapshot.Available || snapshot.Persistence != PersistenceStatus.Ready ||
            snapshot.State is null || snapshot.Configuration is null || snapshot.State.Paused || NeedsSetup || commandPending;
        RecordedQualifier = committed.Recorded is not null && held ? "Last recorded" : "";
        ReminderLabel = ReminderValue = ""; ShowExhaustionExplanation = false;
        ShowUsageProgress = false;
        if (committed.Recorded is not { State: var state, Configuration: var config })
        {
            HeroLabel = "Personal time remaining"; HeroValue = "—"; Arithmetic = "Recorded usage unavailable";
            BreakValue = "—"; BreakExplanation = "Recorded break progress unavailable";
            return;
        }
        var over = Math.Max(0, state.UsageMs - config.AllowanceMs);
        ShowUsageProgress = !state.Exhausted;
        HeroLabel = state.Exhausted ? over > 0 ? "Over allowance" : "Allowance exhausted" : "Personal time remaining";
        HeroValue = state.Exhausted && over == 0 ? "—" : CommittedPresentation.Duration(state.Exhausted ? over : Math.Max(0, config.AllowanceMs - state.UsageMs), remaining: !state.Exhausted);
        Arithmetic = $"{CommittedPresentation.Duration(state.UsageMs)} used · {CommittedPresentation.Duration(config.AllowanceMs, remaining: true)} allowance";
        ShowExhaustionExplanation = state.Exhausted && over == 0;
        BreakValue = $"{CommittedPresentation.Duration(state.BreakProgressMs)} of {CommittedPresentation.Duration(config.RequiredBreakMs, remaining: true)}";
        BreakExplanation = state.BreakCompleted ? "Break completed; allowance reset" : held ? "Last recorded qualifying break" :
            currentContext && snapshot.Context == DesktopScope.Work ? "Verified Work time contributes to your break" : "Qualifying break progress";
        if (state.Exhausted && snapshot.Settings.ReminderStyle != ReminderStyle.Off &&
            state.Intervention is InterventionState.PersonalDeferred or InterventionState.Nudging)
        {
            ReminderLabel = state.Intervention == InterventionState.PersonalDeferred ? "Extra Personal time remaining" : "Next reminder";
            ReminderValue = CommittedPresentation.Duration(state.Intervention == InterventionState.PersonalDeferred ? state.SnoozeRemainingMs : state.ReminderRemainingMs, remaining: true);
        }
    }
    private static string AssignmentText(string role, Guid? id, AssignmentStatus status) => role + " desktop: " +
        (status switch
        {
            AssignmentStatus.Unassigned => "Incomplete — unassigned",
            AssignmentStatus.Valid => "Completed — assigned and observed",
            AssignmentStatus.Missing => "Invalid — assigned desktop no longer available; reassign explicitly",
            AssignmentStatus.Invalid => "Invalid — choose two distinct desktops",
            _ => id is null ? "Unavailable — setup cannot be validated" : "Unavailable — assignment saved; awaiting fresh validation"
        });
    public static string Duration(long milliseconds) => CommittedPresentation.Duration(milliseconds);
}
