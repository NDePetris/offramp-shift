using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopFeasibility;

public sealed record Desktop(Guid Id, string Name);
public sealed record Configuration(string Build, string Architecture, int SessionId, int MonitorCount);
public sealed record Snapshot(
    Configuration Configuration, string Provider, DateTimeOffset ObservedUtc, long ObservedUptimeMs,
    Guid Current, Desktop[] Desktops, long ForegroundWindow, int ShellProcessId,
    bool Interactive, Guid? ForegroundDesktop, bool? ForegroundOnCurrent);
public sealed record Bindings(int Version, string Provider, string Build, Guid? Personal, Guid? Work);
public sealed record SwitchRequest(Guid RequestId, Snapshot Expected, Guid Target, bool Confirmed);
public sealed record SwitchResult(Guid RequestId, string Status, Guid? Observed, bool Attempted, long ElapsedMs);
public sealed record WorkerRequest(string Command, SwitchRequest? Switch = null, int DelayMs = 0);
public sealed record WorkerReply(bool Ok, Snapshot? Snapshot = null, SwitchResult? Switch = null, string? Error = null);

public static class Wire
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidDataException("Empty JSON document.");
}

public static class Capabilities
{
    public const string Provider = "shell-com-53f5ca0b-spike-v1";
    // Experimental allowlist only. This is NOT a production support declaration.
    public static bool MayProbe(Configuration c) => c.Build == "26200.9457" && c.Architecture == "X64";
}

public static class Safety
{
    public static void ValidateSnapshot(Snapshot s, bool qualification = false)
    {
        if (!SwitchCompatibility.Allowed(s.Configuration, qualification)) throw new InvalidOperationException("Unsupported exact build/architecture.");
        if (s.Provider != Capabilities.Provider) throw new InvalidOperationException("Unknown provider.");
        if (s.Desktops.Length is < 1 or > 64 || s.Desktops.Any(d => d.Id == Guid.Empty)
            || s.Desktops.Select(d => d.Id).Distinct().Count() != s.Desktops.Length
            || !s.Desktops.Any(d => d.Id == s.Current))
            throw new InvalidOperationException("Invalid or changing desktop topology.");
        if (s.Configuration.MonitorCount < 1 || s.ShellProcessId <= 0)
            throw new InvalidOperationException("Missing shell/monitor evidence.");
    }

    public static void ValidateBindings(Bindings b, Snapshot s, bool complete)
    {
        ValidateSnapshot(s);
        if (b.Version != 1 || b.Provider != s.Provider || b.Build != s.Configuration.Build)
            throw new InvalidOperationException("Binding version/provider/build requires revalidation.");
        if (b.Personal == Guid.Empty || b.Work == Guid.Empty || (b.Personal.HasValue && b.Personal == b.Work))
            throw new InvalidOperationException("Personal and Work must be distinct, nonempty desktop identities.");
        if (complete && (!b.Personal.HasValue || !b.Work.HasValue))
            throw new InvalidOperationException("Both roles must be bound.");
        foreach (var id in new[] { b.Personal, b.Work }.Where(x => x.HasValue))
            if (!s.Desktops.Any(d => d.Id == id)) throw new InvalidOperationException("Bound desktop was deleted; no substitution allowed.");
    }

    public static Guid Target(Bindings b, Snapshot s, string role)
    {
        ValidateBindings(b, s, true);
        return role switch
        {
            "Personal" when s.Current == b.Work => b.Personal!.Value,
            "Work" when s.Current == b.Personal => b.Work!.Value,
            _ => throw new InvalidOperationException("Switch requires the opposite bound source desktop.")
        };
    }

    public static string? Reject(SwitchRequest r, Snapshot actual, DateTimeOffset now, long uptimeMs, bool qualification = false)
    {
        try { ValidateSnapshot(r.Expected, qualification); ValidateSnapshot(actual, qualification); }
        catch (InvalidOperationException ex) { return ex.Message; }
        if (!r.Confirmed || r.RequestId == Guid.Empty) return "Explicit experimental confirmation required.";
        // Sleep-inclusive uptime is comparable across workers in the same OS session.
        // UTC is an additional uncertainty check, never the sole freshness authority.
        var monotonicAge = uptimeMs - r.Expected.ObservedUptimeMs;
        if (r.Expected.ObservedUptimeMs < 0 || monotonicAge is < 0 or > 2000) return "Expired monotonic request lease.";
        var age = now - r.Expected.ObservedUtc;
        if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(2)) return "Stale request or clock uncertainty.";
        if (r.Expected.Configuration != actual.Configuration || r.Expected.Provider != actual.Provider)
            return "Configuration changed.";
        if (!r.Expected.Interactive || !actual.Interactive) return "Session is not confirmed interactive.";
        if (r.Expected.ForegroundWindow == 0 || actual.ForegroundWindow == 0) return "Foreground is unavailable.";
        if (r.Expected.Current != actual.Current) return "User changed desktops.";
        if (r.Expected.ShellProcessId != actual.ShellProcessId) return "Explorer restarted.";
        if (r.Expected.ForegroundWindow != actual.ForegroundWindow) return "Foreground changed.";
        if (!r.Expected.Desktops.Select(d => d.Id).SequenceEqual(actual.Desktops.Select(d => d.Id)))
            return "Desktop topology changed.";
        if (r.Target == Guid.Empty || r.Target == actual.Current || !actual.Desktops.Any(d => d.Id == r.Target))
            return "Destination missing or already current.";
        return null;
    }
}

public interface IDesktopSession
{
    Snapshot Observe();
    Guid ResolveIdentity(Guid target);
    void SwitchByIdentity(Guid target);
}

public static class SwitchExperiment
{
    // No retry and no corrective switch, even after a timeout or wrong result.
    public static SwitchResult Run(SwitchRequest request, IDesktopSession session,
        Func<DateTimeOffset> utc, Func<long> uptimeMs, Func<long> elapsedMs, Action<int> delay, bool qualification = false)
    {
        var start = elapsedMs();
        var attempted = false;
        try
        {
            var rejection = Safety.Reject(request, session.Observe(), utc(), uptimeMs(), qualification);
            if (rejection != null) return Result("Rejected: " + rejection, null);
            if (session.ResolveIdentity(request.Target) != request.Target)
                return Result("Rejected: resolved identity mismatch", null);
            rejection = Safety.Reject(request, session.Observe(), utc(), uptimeMs(), qualification);
            if (rejection != null) return Result("Rejected: " + rejection, null);
            attempted = true;
            session.SwitchByIdentity(request.Target);
            while (elapsedMs() - start < 2000)
            {
                var observed = session.Observe();
                Safety.ValidateSnapshot(observed, qualification);
                if (observed.Configuration != request.Expected.Configuration || observed.Provider != request.Expected.Provider
                    || observed.ShellProcessId != request.Expected.ShellProcessId || !observed.Interactive
                    || !observed.Desktops.Select(d => d.Id).SequenceEqual(request.Expected.Desktops.Select(d => d.Id)))
                    return Result("Indeterminate: verification environment changed", observed.Current);
                if (observed.Current == request.Target) return Result("Verified", observed.Current);
                if (observed.Current != request.Expected.Current) return Result("WrongDestinationOrUserRace", observed.Current);
                delay(25);
            }
            return Result("Indeterminate: verification timeout", null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result((attempted ? "Indeterminate: " : "Rejected: ") + ex.GetType().Name + ": " + ex.Message, null);
        }
        SwitchResult Result(string status, Guid? observed) => new(request.RequestId, status, observed, attempted, elapsedMs() - start);
    }
}
