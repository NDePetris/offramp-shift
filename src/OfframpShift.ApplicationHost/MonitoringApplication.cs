using OfframpShift.Core;
using OfframpShift.Persistence;
using WindowsObservation;
using DesktopFeasibility;

namespace OfframpShift.ApplicationHost;

// Deterministic owner-thread application state. No Dispatcher, callbacks or timers.
internal sealed class MonitoringApplication : IDisposable
{
    private readonly IMonitoringClock clock;
    private readonly Action newEpoch;
    private readonly Func<long> elapsed;
    private readonly Func<IObservationSource> providers;
    private readonly Func<DurableMonitoringSession> openSession;
    private readonly WindowsSettingsStore settingsStore;
    private readonly bool supported;
    private readonly string build;
    private readonly Func<Configuration> switchConfiguration;
    private DurableMonitoringSession session;
    private WindowsSettings settings;
    private MonitoringRuntime? runtime;
    private bool providerFault;
    private string message = "";
    private long sequence, lastProviderAttempt = -10_000;
    private Guid accountingPresentationEpoch = Guid.NewGuid();
    private SwitchAuthorization? pendingSwitch;
    internal BreakReminderMailbox Reminders { get; } = new();
    internal object? ObservationDiagnostics => runtime?.Diagnostics;
    internal ReminderAuthorizationCapture AuthorizationCapture { get; }
    internal long Elapsed => elapsed();
    internal long NextSamplingDeadline => runtime?.NextSamplingDeadline ?? long.MaxValue;

    public MonitoringApplication(IMonitoringClock clock, Action newEpoch, Func<long> elapsed,
        Func<IObservationSource> providers, Func<DurableMonitoringSession> openSession,
        WindowsSettingsStore settingsStore, bool supported, string build, Func<Configuration>? switchConfiguration = null)
    {
        this.clock = clock; this.newEpoch = newEpoch; this.elapsed = elapsed;
        this.providers = providers; this.openSession = openSession; this.settingsStore = settingsStore;
        this.supported = supported; this.build = build;
        AuthorizationCapture = new(build);
        this.switchConfiguration = switchConfiguration ?? (() => new(build, "Unsupported", -1, 0));
        settings = settingsStore.Load();
        // Persist revocation before opening accounting on a different consent scope.
        // A save failure aborts startup rather than resurrecting a dormant grant.
        if (settingsStore.Status == SettingsStatus.Ready && settings.SwitchingConsent && !settings.HasSwitchingConsent(build))
            SaveSettings(settings with { SwitchingConsent = false, SwitchingConsentBuild = "", SwitchingConsentFamily = "" });
        session = openSession();
        EnsureRuntime();
    }

    private bool Ready => session.Status == PersistenceStatus.Ready;
    private bool ObservationConsentMatches => DesktopObservationPolicy.SameConsentScope(settings.Build, build);
    private bool CanObserve => supported && settings.Consent && ObservationConsentMatches &&
        settingsStore.Status == SettingsStatus.Ready && Ready;
    private bool CanAccount => CanObserve && settings.Complete && settings.MonitoringEnabled &&
        session.Configuration.Accounting == AccountingMode.BestEffort;

    private void EnsureRuntime()
    {
        if (!CanObserve || runtime is not null || providerFault) return;
        try
        {
            runtime = new(clock, newEpoch, elapsed, providers, session,
                settings.Personal ?? Guid.NewGuid(), settings.Work ?? Guid.NewGuid())
            { Reminders = Reminders };
        }
        catch { providerFault = true; message = "Observation provider initialization failed. Retry monitoring after Windows settles."; }
    }

    public void Step()
    {
        EnsureRuntime();
        if (runtime is null || providerFault) { Reminders.Invalidate(); return; }
        try
        {
            runtime.Step(CanAccount, settings.ReminderStyle != ReminderStyle.Off, settings.ReminderStyle == ReminderStyle.InteractiveWindow);
            runtime.RevalidateSwitchAuthorization();
        }
        catch { runtime.InvalidateSwitch(); Reminders.Invalidate(); providerFault = true; message = "Observation or cleanup failed. Accounting stopped. Retry monitoring explicitly."; }
    }

    private void SaveSettings(WindowsSettings value) { settingsStore.Save(value); settings = value; }
    private void ResetRuntime()
    {
        if (runtime is null) return;
        try { runtime.Dispose(); runtime = null; }
        catch { providerFault = true; throw; }
    }

    public HostCommandResult Execute(HostCommand command)
    {
        pendingSwitch?.Revoke(); pendingSwitch = null;
        Reminders.Invalidate();
        if (command.Kind is not (HostCommandKind.BeginNudgePresentation or HostCommandKind.CompleteNudgePresentation or HostCommandKind.AuthorizeSwitch)) runtime?.RevokePresentation();
        message = "";
        try
        {
            if (command.Kind is HostCommandKind.ChangeDurations or HostCommandKind.ChangeInteractiveRepeat)
                Require(Ready, "Storage is unavailable.");
            if (command.Kind is HostCommandKind.ChangeAppearance or HostCommandKind.ChangeBreakReminders or HostCommandKind.ChangeSwitchingConsent)
                Require(settingsStore.Status is SettingsStatus.Ready or SettingsStatus.Missing, "Windows settings are unavailable. Review settings storage before saving.");
            if (command.Expected is { } expectedSettings && !expectedSettings.Matches(command.Kind, settings, session.Configuration))
                return HostCommandResult.Failure("These settings changed while you were editing. Your draft is retained. Cancel to load the confirmed values, then review your changes.") with { ConfigurationConflict = true };
            switch (command.Kind)
            {
                case HostCommandKind.Consent:
                    Require(command.Confirmed && supported && Ready, "Experimental observation requires explicit consent and available storage.");
                    SaveSettings(settings with { Consent = true, Build = build, SwitchingConsent = false, SwitchingConsentBuild = "", SwitchingConsentFamily = "" });
                    EnsureRuntime(); break;
                case HostCommandKind.Start:
                    Require(CanObserve && !providerFault && settings.Complete && BindingsValid(), "Review consent and assign two distinct, currently observed desktops before starting.");
                    // Settings intent alone cannot enable accounting in CertifiedOnly mode.
                    SaveSettings(settings with { MonitoringEnabled = true });
                    if (session.Configuration.Accounting != AccountingMode.BestEffort)
                        session.ChangeConfiguration(session.Configuration with { Accounting = AccountingMode.BestEffort });
                    if (session.State.Paused) session.SetPaused(false);
                    runtime?.ResetConfiguration(); break;
                case HostCommandKind.Pause:
                case HostCommandKind.Resume:
                    Require(Ready, "Storage is unavailable.");
                    if (command.Kind == HostCommandKind.Resume)
                        Require(CanAccount && BindingsValid(), "Monitoring setup or providers are unavailable.");
                    session.SetPaused(command.Kind == HostCommandKind.Pause);
                    runtime?.ResetConfiguration(); break;
                case HostCommandKind.RetrySave:
                    Require(session.Status == PersistenceStatus.PendingCommit, "No pending commit.");
                    accountingPresentationEpoch = Guid.NewGuid();
                    session.RetryPendingCommit();
                    runtime?.ResetConfiguration(); EnsureRuntime(); break;
                case HostCommandKind.RetryMonitoring:
                    Require(CanObserve, "Observation requires a supported build, consent and available storage.");
                    Require(elapsed() - lastProviderAttempt >= 10_000 && (runtime?.RetryInMs ?? 0) == 0,
                        "Wait ten seconds between provider retries.");
                    lastProviderAttempt = elapsed();
                    try
                    {
                        if (providerFault)
                        { ResetRuntime(); providerFault = false; EnsureRuntime(); }
                        else if (runtime is not null) Require(runtime.RetryProviders(), "Wait ten seconds between provider retries.");
                        else EnsureRuntime();
                    }
                    catch { providerFault = true; throw; }
                    Require(!providerFault && runtime is not null,
                        "Observation providers could not be restarted. Wait ten seconds, then retry monitoring after Windows settles. Accounting remains unavailable.");
                    message = "Observation providers restarted. Waiting for fresh evidence; monitoring availability is not yet confirmed.";
                    break;
                case HostCommandKind.AssignPersonal:
                case HostCommandKind.AssignWork:
                    Require(CanObserve && !providerFault && runtime is not null,
                        "Desktop observation is unavailable. Review consent and storage in Settings; retry monitoring after Windows settles.");
                    // Fresh capture at command execution; never activate the main window first.
                    var point = runtime!.CaptureCurrent();
                    Require(point is not null && Fresh(point) && point.Desktop.Health == Capability.Available &&
                        point.Desktop.Current is not null && point.Desktop.Desktops.Contains(point.Desktop.Current.Value),
                        "No fresh current desktop is available. Try again after Windows settles.");
                    var replacement = command.Kind == HostCommandKind.AssignPersonal
                        ? settings with { Personal = point!.Desktop.Current, MonitoringEnabled = false }
                        : settings with { Work = point!.Desktop.Current, MonitoringEnabled = false };
                    replacement = replacement with { SwitchingConsent = false, SwitchingConsentBuild = "", SwitchingConsentFamily = "" };
                    Require(replacement.Personal != replacement.Work,
                        "Personal and Work must use different desktops. Choose the other desktop in Task View, then assign it through the tray menu.");
                    replacement.Validate();
                    accountingPresentationEpoch = Guid.NewGuid();
                    ResetRuntime();
                    SaveSettings(replacement);
                    // Reuse restoration to clear unfinished Core continuity; do not invent a domain command.
                    session.Dispose(); session = openSession(); EnsureRuntime();
                    message = "Desktop assignment saved. Start monitoring when both assignments are ready.";
                    break;
                case HostCommandKind.ChangeDurations:
                    Require(Ready, "Storage is unavailable.");
                    var configuration = session.Configuration with
                    {
                        AllowanceMs = command.AllowanceMs,
                        RequiredBreakMs = command.RequiredBreakMs,
                        IdleThresholdMs = command.IdleThresholdMs
                    };
                    configuration.Validate();
                    session.ChangeConfiguration(configuration); runtime?.ResetConfiguration(); break;
                case HostCommandKind.ChangeAppearance:
                    SaveSettings(settings with { Theme = command.Theme, CloseToTray = command.CloseToTray }); break;
                case HostCommandKind.ChangeBreakReminders:
                    SaveSettings(settings with { ReminderStyle = command.ReminderStyle }); break;
                case HostCommandKind.ChangeSwitchingConsent:
                    Require(!command.Confirmed || CanObserve && settings.Complete,
                        "Review desktop observation consent and both assignments first.");
                    SaveSettings(settings with
                    {
                        SwitchingConsent = command.Confirmed,
                        SwitchingConsentBuild = command.Confirmed && !SwitchCompatibility.Is26300Family(build) ? build : "",
                        SwitchingConsentFamily = command.Confirmed && SwitchCompatibility.Is26300Family(build) ? "26300" : ""
                    });
                    message = "Switching preference saved. Each attempt still requires approved capability and fresh authorization."; break;
                case HostCommandKind.AuthorizeSwitch:
                    Require(CanAccount && !providerFault && settings.ReminderStyle == ReminderStyle.InteractiveWindow &&
                        settings.HasSwitchingConsent(build) && BindingsValid(),
                        "Enable separate experimental switching consent for this build in Settings and review assignments.");
                    var nativeConfiguration = switchConfiguration();
                    Require(nativeConfiguration.Build == build && SwitchCompatibility.Allowed(nativeConfiguration, false),
                        "Application switching is disabled on this Windows build/architecture.");
                    var evidence = runtime?.AuthorizeSwitch(command.NudgeWindow);
                    Require(evidence is not null, "The active eligible Personal nudge and fresh foreground evidence are required.");
                    var age = Environment.TickCount64 - evidence!.UptimeMs;
                    Require(age is >= 0 and <= 2_000, "Switch authorization evidence expired.");
                    var expected = new DesktopFeasibility.Snapshot(nativeConfiguration, Capabilities.Provider,
                        DateTimeOffset.UtcNow.AddMilliseconds(-age), evidence.UptimeMs, settings.Personal!.Value,
                        evidence.Desktop.Desktops.Select(id => new Desktop(id, "")).ToArray(), command.NudgeWindow.ToInt64(),
                        checked((int)evidence.Desktop.ShellProcess), true, null, null);
                    var request = new AuthorizedSwitch(new(Guid.NewGuid(), expected, settings.Work!.Value, true), settings.Personal.Value, settings.Work.Value);
                    pendingSwitch = new(request, evidence.UptimeMs + 2_000);
                    runtime!.TrackSwitchAuthorization(pendingSwitch);
                    return HostCommandResult.Success() with { Switching = pendingSwitch };
                case HostCommandKind.ChangeInteractiveRepeat:
                    Require(Ready, "Storage is unavailable.");
                    session.ChangeInteractiveRepeat(command.DurationMs);
                    runtime?.RefreshInterventionConfiguration(); break;
                case HostCommandKind.BeginNudgePresentation:
                    var capture = AuthorizationCapture.TryBegin();
                    try
                    {
                        Require(CanAccount && !providerFault && settings.ReminderStyle == ReminderStyle.InteractiveWindow &&
                            command.Reminder is not null && runtime is not null, "Monitoring or reminder style is no longer eligible.");
                        var presentation = runtime!.BeginNudgePresentation(command.Reminder!, capture);
                        Require(presentation is not null, runtime.NudgeAuthorizationFailure);
                        if (capture is not null) { capture.Authorized = true; capture.Stage = AuthorizationCaptureStage.Authorized; }
                        return HostCommandResult.Success() with { Presentation = presentation };
                    }
                    finally { if (capture is not null) AuthorizationCapture.Complete(capture); }
                case HostCommandKind.CompleteNudgePresentation:
                    Require(CanAccount && !providerFault && settings.ReminderStyle == ReminderStyle.InteractiveWindow &&
                        command.Presentation is not null && runtime is not null, "Monitoring or reminder style is no longer eligible.");
                    Require(runtime!.CompleteNudgePresentation(command.Presentation!, command.NudgeWindow), runtime.NudgeAuthorizationFailure);
                    return HostCommandResult.Success() with { Presentation = command.Presentation };
                case HostCommandKind.DeferInteractive:
                    Require(CanAccount && !providerFault && settings.ReminderStyle == ReminderStyle.InteractiveWindow &&
                        Ready && session.State.Exhausted && !session.State.Paused && runtime?.CanDefer(command.NudgeWindow) == true,
                        "Extra time is unavailable in the current context.");
                    session.DeferInteractive(command.DurationMs);
                    runtime?.ResetConfiguration();
                    message = "Extra Personal time saved; usage continues."; break;
                case HostCommandKind.ResetUsage:
                    Require(command.Confirmed && Ready, "Confirm Reset usage; ready accounting storage is required.");
                    accountingPresentationEpoch = Guid.NewGuid();
                    session.ResetUsage();
                    runtime?.ResetConfiguration();
                    message = "Usage and break progress reset and saved. Pause and monitoring settings are unchanged.";
                    break;
                case HostCommandKind.ChangeCloseBehavior:
                    Require(command.CloseToTray is not null, "Choose whether closing keeps the app in the tray or exits.");
                    SaveSettings(settings with { CloseToTray = command.CloseToTray }); break;
                case HostCommandKind.ReopenStorage:
                    Require(session.Status == PersistenceStatus.Blocked, "Reopen is only available for blocked storage.");
                    accountingPresentationEpoch = Guid.NewGuid();
                    ResetRuntime(); session.Dispose(); session = openSession(); EnsureRuntime(); break;
                case HostCommandKind.Recover:
                    Require(command.Confirmed && session.Status == PersistenceStatus.RecoveryRequired,
                        "Explicit recovery confirmation is required; recovery is unavailable in this state.");
                    accountingPresentationEpoch = Guid.NewGuid();
                    ResetRuntime();
                    session.Reinitialize(new());
                    Reminders.ResetSession();
                    if (settingsStore.Status is SettingsStatus.Ready or SettingsStatus.Missing)
                        SaveSettings(settings with { MonitoringEnabled = false });
                    EnsureRuntime(); break;
                case HostCommandKind.RepairSettings:
                    settingsStore.Repair(command.Confirmed); settings = settingsStore.Load(); break;
                default: throw new ArgumentException("Unknown command.");
            }
        }
        catch (CheckpointStorageException e)
        { message = StorageDetail(session.Status, e.Failure); return HostCommandResult.Failure(message); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            message = providerFault && command.Kind == HostCommandKind.RetryMonitoring ?
                "Observation providers could not be restarted. Wait ten seconds, then retry monitoring after Windows settles. Accounting remains unavailable." :
                e is ArgumentException ? "Check the settings: durations must be valid and desktop assignments must be distinct." :
                e is IOException or UnauthorizedAccessException ? "Windows settings could not be saved. Existing saved settings remain in effect. Check local storage access and try again." : e.Message;
            return HostCommandResult.Failure(message);
        }
        return HostCommandResult.Success(message);
    }

    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    private bool Fresh(EvidencePoint point) => clock.Now.Epoch == point.Time.Epoch &&
        clock.Now.Milliseconds - point.Time.Milliseconds is >= 0 and <= 2_000;
    private bool BindingsValid() => runtime?.Latest is { } p && Fresh(p) && p.Desktop.Health == Capability.Available &&
        p.Desktop.ShellProcess != 0 && p.Desktop.Current is { } current && p.Desktop.Desktops.Contains(current) &&
        settings.Personal is { } personal && settings.Work is { } work && personal != work &&
        p.Desktop.Desktops.Contains(personal) && p.Desktop.Desktops.Contains(work);

    private bool DesktopObserved => CanObserve && !providerFault && runtime?.Latest is { } p && Fresh(p) &&
        p.Desktop.Health == Capability.Available && p.Desktop.ShellProcess != 0 &&
        p.Desktop.Current is { } current && current != Guid.Empty && p.Desktop.Desktops.Contains(current);

    private AssignmentStatus Assignment(Guid? id) => settingsStore.Status is not (SettingsStatus.Ready or SettingsStatus.Missing) ? AssignmentStatus.Unavailable :
        id is null ? AssignmentStatus.Unassigned :
        id == Guid.Empty || settings.Personal == settings.Work ? AssignmentStatus.Invalid :
        !DesktopObserved ? AssignmentStatus.Unavailable :
        runtime!.Latest!.Desktop.Desktops.Contains(id.Value) ? AssignmentStatus.Valid : AssignmentStatus.Missing;

    public HostSnapshot Snapshot()
    {
        var state = Ready ? session.State : null;
        var config = Ready ? session.Configuration : null;
        var point = runtime?.Latest;
        var bindings = BindingsValid();
        var context = point is not null && Fresh(point) && bindings ?
            point.Desktop.Current == settings.Personal ? DesktopScope.Personal :
            point.Desktop.Current == settings.Work ? DesktopScope.Work : DesktopScope.Other : DesktopScope.Unknown;
        var available = !providerFault && runtime?.Health.Healthy == true && bindings && point is not null && Fresh(point) &&
            point.SessionHealth == Capability.Available && (point.Session == SessionState.Locked ||
            point.Session == SessionState.Active && point.Input == Capability.Available && (context != DesktopScope.Personal || point.Foreground == Capability.Available));
        var status = !Ready ? session.Status.ToString() : !supported ? "Unavailable" :
            settingsStore.Status is not (SettingsStatus.Ready or SettingsStatus.Missing) ? "Settings unavailable" :
            !settings.Consent || !settings.Complete ? "Setup required" : state!.Paused ? "Paused" :
            !available ? "Unavailable" : !CanAccount ? "Ready to start" :
            runtime?.Reason is "AwaitingBaseline" or "FreshBaseline" or "CoreRejectedFreshBaselineRequired" ? "Establishing baseline" :
            runtime?.Reason != "BoundedEstimate" ? "Waiting for evidence" :
            state.Intervention == InterventionState.Nudging ? "Nudging" : state.Exhausted ? "Allowance exhausted" : "Monitoring";
        var detail = !Ready ? StorageDetail(session.Status, session.Failure) : !supported ?
            "Desktop observation requires an approved Windows build or build family on x64. This build or architecture is unsupported; no accounting is available." :
            !ObservationConsentMatches && settings.Consent ? "The saved desktop provider build differs. Explicitly review setup again." :
            providerFault ? "Observation provider failed. Retry monitoring explicitly after Windows settles." :
            !settings.Consent ? "Opt in to experimental Estimated monitoring to observe and assign desktops." :
            !bindings ? "Both assigned desktops must be present in a fresh observation. Use Task View and the tray menu to assign distinct desktops; deleted desktops are never substituted." :
            point?.Foreground == Capability.Unavailable && context == DesktopScope.Personal && point.Session == SessionState.Active ?
            $"Foreground identity unavailable: {point.ForegroundFailure}. This interval earns no credit." :
            state!.Paused ? "Monitoring is paused. Usage and break progress do not advance." :
            !available ? "Waiting for fresh healthy observations. Unknown time earns no usage or break credit." :
            runtime?.Reason != "BoundedEstimate" && CanAccount ? "Waiting for fresh interval evidence. Transition, stale and unknown intervals earn no credit." :
            "Estimated monitoring; owner evaluation only. Explorer recovery and sustained accounting reliability remain unresolved.";
        return new(++sequence, Environment.TickCount64, session.Status, session.Failure, settingsStore.Status, settings,
            state, config, context, status, message.Length > 0 ? message + " " + detail : detail,
            available, bindings, Math.Max(runtime?.RetryInMs ?? 0, Math.Max(0, 10_000 - (elapsed() - lastProviderAttempt))), CanObserve,
            supported && Ready && settingsStore.Status is SettingsStatus.Ready or SettingsStatus.Missing &&
                (!settings.Consent || !ObservationConsentMatches), DesktopObserved,
            Assignment(settings.Personal), Assignment(settings.Work), settings.Consent && !ObservationConsentMatches,
            CanAccount && !providerFault && settings.ReminderStyle == ReminderStyle.InteractiveWindow && runtime?.CanKeepNudgeOpen == true,
            runtime?.ReminderGeneration ?? 0, runtime?.ReminderEpoch ?? Guid.Empty,
            runtime?.ReminderStatus ?? "Observation unavailable; no reminder delivery.", runtime?.PresentationId ?? Guid.Empty)
        { SwitchingUnavailableReason = SwitchingUnavailableReason, AccountingPresentationEpoch = accountingPresentationEpoch };
    }

    private string SwitchingUnavailableReason =>
        !supported || !SwitchCompatibility.Application(build, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString())
            ? "Switching is not approved for this Windows build or architecture." :
        !settings.HasSwitchingConsent(build)
            ? "Enable experimental switching consent for this build or build family in Settings." :
        !CanAccount || providerFault || settings.ReminderStyle != ReminderStyle.InteractiveWindow || runtime?.CanKeepNudgeOpen != true
            ? "Switching requires an active Personal nudge and healthy monitoring." : "";

    private static string StorageDetail(PersistenceStatus status, StorageFailure? failure) => status switch
    {
        PersistenceStatus.PendingCommit => $"Save not acknowledged ({failure}). Accounting is stopped. Retry save retries the exact checkpoint; delayed effects are discarded.",
        PersistenceStatus.RecoveryRequired => $"Accounting storage requires recovery ({failure}). Reset may lose accumulated accounting; originals are preserved locally before replacement.",
        PersistenceStatus.Incompatible => "Storage belongs to an unsupported version. Use a compatible app; reset is unavailable.",
        _ => $"Storage is blocked ({failure}). Correct clock, access or competing-owner problems, then reopen storage. No accounting or reset is available."
    };

    public void Dispose() { AuthorizationCapture.Stop(); pendingSwitch?.Revoke(); Reminders.Invalidate(); try { ResetRuntime(); } finally { session.Dispose(); } }
}
