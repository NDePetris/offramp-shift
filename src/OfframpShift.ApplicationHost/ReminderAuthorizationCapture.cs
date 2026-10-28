using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WindowsObservation;

namespace OfframpShift.ApplicationHost;

public enum AuthorizationCaptureState { Idle, Armed, Capturing, Complete, Stopped }
public enum AuthorizationCaptureStage { HostPrerequisites, EventBeforeDesktopCheck, WorkerCheck, EventAfterDesktopCheck, Authorized }
public sealed record AuthorizationProvenance(string WindowsBuild, string OsArchitecture, string ProcessArchitecture,
    string DiagnosticSourceRevision, string RunningApplicationRevision);
public sealed record AuthorizationCaptureReport(AuthorizationProvenance Provenance, AuthorizationCaptureStage Stage,
    bool Authorized, NudgeDesktopDiagnostic? Worker);
public sealed record AuthorizationCaptureSnapshot(AuthorizationCaptureState State, AuthorizationCaptureReport? Report)
{
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    { Converters = { new JsonStringEnumConverter() }, WriteIndented = true });
}

internal sealed class AuthorizationCaptureAttempt
{
    internal AuthorizationCaptureStage Stage = AuthorizationCaptureStage.HostPrerequisites;
    internal bool Authorized;
    internal NudgeDesktopDiagnostic? Worker;
}

// The UI arms an in-memory latch, not a host command. No accounting invalidation or wakeup.
public sealed class ReminderAuthorizationCapture(string build)
{
    private readonly object gate = new();
    private AuthorizationCaptureState state;
    private AuthorizationCaptureReport? report;
    private AuthorizationProvenance? provenance;
    private AuthorizationCaptureAttempt? active;
    public AuthorizationCaptureSnapshot Snapshot { get { lock (gate) return new(state, report); } }

    public bool Arm()
    {
        lock (gate)
        {
            if (state is AuthorizationCaptureState.Armed or AuthorizationCaptureState.Capturing or AuthorizationCaptureState.Stopped) return false;
            // Reflection reads loaded assembly metadata; never infer a running revision from the checkout.
            provenance = new(build, RuntimeInformation.OSArchitecture.ToString(), RuntimeInformation.ProcessArchitecture.ToString(),
                EmbeddedRevision(typeof(NudgeDesktopDiagnostic).Assembly),
                Assembly.GetEntryAssembly() is { } entry && entry.GetName().Name == "OfframpShift.App" ? EmbeddedRevision(entry) : "Unknown");
            report = null; state = AuthorizationCaptureState.Armed; return true;
        }
    }

    internal static string EmbeddedRevision(Assembly? assembly)
    {
        var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var match = Regex.Match(version, @"\+([0-9a-fA-F]{40})$");
        return match.Success ? match.Groups[1].Value : "Unknown";
    }

    internal AuthorizationCaptureAttempt? TryBegin()
    {
        lock (gate)
        {
            if (state != AuthorizationCaptureState.Armed) return null;
            state = AuthorizationCaptureState.Capturing;
            return active = new();
        }
    }

    internal void Complete(AuthorizationCaptureAttempt attempt)
    {
        lock (gate)
        {
            if (state != AuthorizationCaptureState.Capturing || active != attempt) return;
            report = new(provenance!, attempt.Stage, attempt.Authorized, attempt.Worker);
            active = null; state = AuthorizationCaptureState.Complete;
        }
    }

    internal void Stop()
    {
        lock (gate) { active = null; report = null; provenance = null; state = AuthorizationCaptureState.Stopped; }
    }
}
