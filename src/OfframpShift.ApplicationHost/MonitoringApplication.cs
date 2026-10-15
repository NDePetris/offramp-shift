using OfframpShift.Core;
using OfframpShift.Persistence;
using WindowsObservation;

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
    private DurableMonitoringSession session;
    private WindowsSettings settings;
    private MonitoringRuntime? runtime;
    private bool providerFault;
    private string message = "";
    private long sequence, lastProviderAttempt = -10_000;

    public MonitoringApplication(IMonitoringClock clock, Action newEpoch, Func<long> elapsed,
        Func<IObservationSource> providers, Func<DurableMonitoringSession> openSession,
        WindowsSettingsStore settingsStore, bool supported, string build)
    {
        this.clock = clock; this.newEpoch = newEpoch; this.elapsed = elapsed;
        this.providers = providers; this.openSession = openSession; this.settingsStore = settingsStore;
        this.supported = supported; this.build = build;
        settings = settingsStore.Load(); session = openSession();
        EnsureRuntime();
    }

    private bool Ready => session.Status == PersistenceStatus.Ready;
    private bool CanObserve => supported && settings.Consent && settings.Build == build &&
        settingsStore.Status == SettingsStatus.Ready && Ready;
    private bool CanAccount => CanObserve && settings.Complete && settings.MonitoringEnabled &&
        session.Configuration.Accounting == AccountingMode.BestEffort;

    private void EnsureRuntime()
    {
        if (!CanObserve || runtime is not null || providerFault) return;
        try
        {
            runtime = new(clock, newEpoch, elapsed, providers, session,
                settings.Personal ?? Guid.NewGuid(), settings.Work ?? Guid.NewGuid());
        }
        catch { providerFault = true; message = "Observation provider initialization failed. Retry monitoring after Windows settles."; }
    }

    public void Step()
    {
        EnsureRuntime();
        if (runtime is null || providerFault) return;
        try { runtime.Step(CanAccount); }
        catch { providerFault = true; message = "Observation or cleanup failed. Accounting stopped. Retry monitoring explicitly."; }
    }

    private void SaveSettings(WindowsSettings value) { settingsStore.Save(value); settings = value; }
    private void ResetRuntime()
    {
        if (runtime is null) return;
        try { runtime.Dispose(); runtime = null; }
        catch { providerFault = true; throw; }
    }

    public string Execute(HostCommand command)
    {
        message = "";
        try
        {
            switch (command.Kind)
            {
                case HostCommandKind.Consent:
                    Require(command.Confirmed && supported && Ready, "Experimental observation requires explicit consent and available storage.");
                    SaveSettings(settings with { Consent = true, Build = build });
                    EnsureRuntime(); break;
                case HostCommandKind.Start:
                    Require(Ready && settings.Complete && BindingsValid(), "Assign two distinct, currently observed desktops before starting.");
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
                        else if (runtime is not null) runtime.RetryProviders();
                        else EnsureRuntime();
                    }
                    catch { providerFault = true; throw; }
                    break;
                case HostCommandKind.AssignPersonal:
                case HostCommandKind.AssignWork:
                    Require(CanObserve && !providerFault && runtime is not null, "Desktop observation is unavailable.");
                    // Fresh capture at command execution; never activate the main window first.
                    var point = runtime!.CaptureCurrent();
                    Require(point is not null && Fresh(point) && point.Desktop.Health == Capability.Available &&
                        point.Desktop.Current is not null && point.Desktop.Desktops.Contains(point.Desktop.Current.Value),
                        "No fresh current desktop is available. Try again after Windows settles.");
                    var replacement = command.Kind == HostCommandKind.AssignPersonal
                        ? settings with { Personal = point!.Desktop.Current, MonitoringEnabled = false }
                        : settings with { Work = point!.Desktop.Current, MonitoringEnabled = false };
                    replacement.Validate();
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
                case HostCommandKind.ReopenStorage:
                    Require(session.Status == PersistenceStatus.Blocked, "Reopen is only available for blocked storage.");
                    ResetRuntime(); session.Dispose(); session = openSession(); EnsureRuntime(); break;
                case HostCommandKind.Recover:
                    Require(command.Confirmed && session.Status == PersistenceStatus.RecoveryRequired,
                        "Explicit recovery confirmation is required; recovery is unavailable in this state.");
                    ResetRuntime();
                    session.Reinitialize(new());
                    if (settingsStore.Status is SettingsStatus.Ready or SettingsStatus.Missing)
                        SaveSettings(settings with { MonitoringEnabled = false });
                    EnsureRuntime(); break;
                case HostCommandKind.RepairSettings:
                    settingsStore.Repair(command.Confirmed); settings = settingsStore.Load(); break;
                default: throw new ArgumentException("Unknown command.");
            }
        }
        catch (CheckpointStorageException e)
        { message = StorageDetail(session.Status, e.Failure); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            message = e is ArgumentException ? "Check the settings: durations must be valid and desktop assignments must be distinct." :
            e is IOException or UnauthorizedAccessException ? "Windows settings could not be saved. Existing saved settings remain in effect." : e.Message;
        }
        return message;
    }

    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    private bool Fresh(EvidencePoint point) => clock.Now.Epoch == point.Time.Epoch &&
        clock.Now.Milliseconds - point.Time.Milliseconds is >= 0 and <= 2_000;
    private bool BindingsValid() => runtime?.Latest is { } p && Fresh(p) && p.Desktop.Health == Capability.Available &&
        p.Desktop.ShellProcess != 0 && p.Desktop.Current is { } current && p.Desktop.Desktops.Contains(current) &&
        settings.Personal is { } personal && settings.Work is { } work && personal != work &&
        p.Desktop.Desktops.Contains(personal) && p.Desktop.Desktops.Contains(work);

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
            "Desktop observation is restricted to Windows build 26200.9457 / x64. No accounting is available on this configuration." :
            settings.Build != build && settings.Consent ? "The saved desktop provider build differs. Explicitly review setup again." :
            providerFault ? "Observation provider failed. Retry monitoring explicitly after Windows settles." :
            !settings.Consent ? "Opt in to experimental Estimated monitoring to observe and assign desktops." :
            !bindings ? "Both assigned desktop GUIDs must be present in a fresh observation. Deleted desktops are never substituted." :
            point?.Foreground == Capability.Unavailable && context == DesktopScope.Personal && point.Session == SessionState.Active ?
            $"Foreground identity unavailable: {point.ForegroundFailure}. This interval earns no credit." :
            state!.Paused ? "Monitoring is paused. Usage and break progress do not advance." :
            !available ? "Waiting for fresh healthy observations. Unknown time earns no usage or break credit." :
            runtime?.Reason != "BoundedEstimate" && CanAccount ? "Waiting for fresh interval evidence or idle qualification. Transition, stale and unknown intervals earn no credit." :
            "Estimated monitoring; owner evaluation only. Explorer recovery and sustained accounting reliability remain unresolved.";
        return new(++sequence, Environment.TickCount64, session.Status, session.Failure, settingsStore.Status, settings,
            state, config, context, status, message.Length > 0 ? message + " " + detail : detail,
            available, bindings, Math.Max(runtime?.RetryInMs ?? 0, Math.Max(0, 10_000 - (elapsed() - lastProviderAttempt))), CanObserve);
    }

    private static string StorageDetail(PersistenceStatus status, StorageFailure? failure) => status switch
    {
        PersistenceStatus.PendingCommit => $"Save not acknowledged ({failure}). Accounting is stopped. Retry save retries the exact checkpoint; delayed effects are discarded.",
        PersistenceStatus.RecoveryRequired => $"Accounting storage requires recovery ({failure}). Reset may lose accumulated accounting; originals are preserved locally before replacement.",
        PersistenceStatus.Incompatible => "Storage belongs to an unsupported version. Use a compatible app; reset is unavailable.",
        _ => $"Storage is blocked ({failure}). Correct clock, access or competing-owner problems, then reopen storage. No accounting or reset is available."
    };

    public void Dispose() { try { ResetRuntime(); } finally { session.Dispose(); } }
}
