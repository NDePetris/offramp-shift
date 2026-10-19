using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace DesktopFeasibility;

public enum SwitchOutcome { ConfirmedSuccess, Rejected, VerifiedFailure, Indeterminate }

public sealed record SwitchCompletion(SwitchOutcome Outcome, string Detail)
{
    public const string ManualFallback = "Press Win+Tab and select Work.";
    public string Message => (Outcome switch
    {
        SwitchOutcome.ConfirmedSuccess => "Work was confirmed at the verification point.",
        SwitchOutcome.Rejected => "No switch was attempted. " + Detail,
        SwitchOutcome.VerifiedFailure => "Work was not confirmed at the bounded verification point. A delayed transition is still possible. Check your desktop manually.",
        _ => "A desktop transition may have occurred. Check your actual desktop in Task View (Win+Tab). Switching is disabled for this nudge."
    }) + " " + ManualFallback;
    public static SwitchCompletion Reject(string detail) => new(SwitchOutcome.Rejected, detail);
    public static SwitchCompletion Unknown(string detail) => new(SwitchOutcome.Indeterminate, detail);
}

public static class SwitchCompatibility
{
    internal static string RuntimeArchitecture(string processArchitecture, string osArchitecture) =>
        osArchitecture == "X64" ? processArchitecture : osArchitecture;
    public static bool Is26300Family(string build) => build?.Split('.') is ["26300", var revision] &&
        uint.TryParse(revision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _);
    public static bool Application(string build, string architecture) =>
        (build == "26200.9457" || Is26300Family(build)) && architecture == "X64";
    // This deliberately does NOT extend Capabilities.MayProbe or application support.
    public static bool Qualification(string build, string architecture) => build is "26200.9711" or "26300.9711" && architecture == "X64";
    public static bool Allowed(Configuration configuration, bool qualification) => qualification
        ? Qualification(configuration.Build, configuration.Architecture)
        : Application(configuration.Build, configuration.Architecture);
}

public sealed record AuthorizedSwitch(SwitchRequest Request, Guid Personal, Guid Work)
{
    public bool Valid => Request is { Expected: { } expected } && Personal != Guid.Empty && Work != Guid.Empty &&
        Personal != Work && Request.Target == Work && expected.Current == Personal &&
        Request.Confirmed && Request.RequestId != Guid.Empty;
}

// The owner revokes and the executor consumes the same atomic state. Whichever
// wins first is final; revocation cannot retract an already consumed request.
public sealed class SwitchAuthorization(AuthorizedSwitch request, long expiresTick)
{
    private int consumed;
    public void Revoke() => Interlocked.Exchange(ref consumed, 1);
    public AuthorizedSwitch? Consume(long now) => Interlocked.Exchange(ref consumed, 1) == 0 &&
        now <= expiresTick && now >= request.Request.Expected.ObservedUptimeMs && request.Valid ? request : null;
}

public interface ISwitchExecutor
{
    Task<SwitchCompletion> ExecuteAsync(SwitchAuthorization authorization);
}

public sealed record SwitchWorkerRequest(string Command, AuthorizedSwitch? Authorization = null);
public sealed record SwitchWorkerReply(SwitchResult? Result = null, Snapshot? Snapshot = null, string? Error = null);

public static class SwitchProtocol
{
    public static SwitchCompletion Classify(AuthorizedSwitch request, SwitchWorkerReply reply, int exitCode)
    {
        var result = reply.Result;
        if (exitCode != 0 || reply.Snapshot is not null || reply.Error is not null || result is null ||
            result.RequestId != request.Request.RequestId || result.Status is null || result.ElapsedMs is < 0 or > 3_000)
            return SwitchCompletion.Unknown("Invalid worker response or abnormal exit.");
        if (!result.Attempted && result.Status.StartsWith("Rejected: ", StringComparison.Ordinal) && result.Observed is null &&
            RejectionDetail(result.Status) is { } rejection)
            return SwitchCompletion.Reject(rejection);
        if (!result.Attempted) return SwitchCompletion.Unknown("Inconsistent attempt status.");
        if (result.Status == "Verified" && result.Observed == request.Work)
            return new(SwitchOutcome.ConfirmedSuccess, "Destination GUID verified.");
        if (result.Status == "WrongDestinationOrUserRace" && result.Observed is { } observed &&
            observed != request.Work && request.Request.Expected.Desktops.Any(d => d.Id == observed))
            return SwitchCompletion.Unknown("WrongDestinationOrUserRace: another desktop was observed; the cause is uncertain.");
        return SwitchCompletion.Unknown("Completion could not be verified.");
    }

    private static string? RejectionDetail(string status)
    {
        var detail = status["Rejected: ".Length..];
        // Only fixed policy messages are UI copy. Never echo arbitrary native
        // exception text or worker-provided identities into retained feedback.
        string[] reasons = ["Unsupported exact build/architecture.", "Unknown provider.",
            "Invalid or changing desktop topology.", "Missing shell/monitor evidence.",
            "Explicit experimental confirmation required.", "Expired monotonic request lease.",
            "Stale request or clock uncertainty.", "Configuration changed.",
            "Session is not confirmed interactive.", "Foreground is unavailable.", "User changed desktops.",
            "Explorer restarted.", "Foreground changed.", "Desktop topology changed.",
            "Destination missing or already current.", "resolved identity mismatch"];
        return reasons.Contains(detail, StringComparer.Ordinal) ? detail : null;
    }

    public static T Decode<T>(string text)
    {
        using var document = JsonDocument.Parse(text);
        Check(document.RootElement);
        return Wire.Decode<T>(text);
        static void Check(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                { if (!names.Add(property.Name)) throw new JsonException("Duplicate property."); Check(property.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Check(item);
        }
    }
}

public sealed class IsolatedSwitchExecutor : ISwitchExecutor
{
    public async Task<SwitchCompletion> ExecuteAsync(SwitchAuthorization authorization)
    {
        var request = authorization.Consume(Environment.TickCount64);
        if (request is null) return SwitchCompletion.Reject("The single-use authorization expired or was consumed.");
        if (!SwitchCompatibility.Allowed(request.Request.Expected.Configuration, false))
            return SwitchCompletion.Reject("This exact build is not approved for application switching.");
        return await ExecuteRequestAsync(request, false).ConfigureAwait(false);
    }

    internal static async Task<SwitchCompletion> ExecuteRequestAsync(AuthorizedSwitch request, bool qualification)
    {
        if (!request.Valid || !SwitchCompatibility.Allowed(request.Request.Expected.Configuration, qualification))
            return SwitchCompletion.Reject("Invalid binding or unsupported exact build.");
        try
        {
            var (reply, exit) = await SwitchWorkerProcess.CallAsync(new("switch", request),
                qualification ? "--switch-qualification-worker" : "--switch-worker").ConfigureAwait(false);
            return SwitchProtocol.Classify(request, reply, exit);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { return SwitchCompletion.Unknown("Worker unavailable, invalid response, deadline or cleanup failure."); }
    }
}

public static class SwitchWorkerProcess
{
    public const int MaximumCharacters = 65_536;
    // Offloads process startup as well as IPC. No native/worker wait occupies the owner or Dispatcher.
    public static Task<(SwitchWorkerReply Reply, int Exit)> CallAsync(SwitchWorkerRequest request,
        string mode, int deadlineMs = 3_000) => Task.Run(async () =>
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("Process host unavailable.");
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
            start.ArgumentList.Add(mode);
            using var deadline = new CancellationTokenSource(deadlineMs);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Worker start failed.");
            var output = ReadBounded(child.StandardOutput, deadline.Token);
            var errors = ReadBounded(child.StandardError, deadline.Token);
            try
            {
                var encoded = Wire.Encode(request);
                if (encoded.Length > MaximumCharacters) throw new InvalidDataException("Request oversized.");
                await child.StandardInput.WriteAsync(encoded.AsMemory(), deadline.Token).ConfigureAwait(false);
                child.StandardInput.Close();
                await child.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                var text = await output.ConfigureAwait(false);
                if ((await errors.ConfigureAwait(false)).Length != 0) throw new InvalidDataException("Unexpected worker error output.");
                return (SwitchProtocol.Decode<SwitchWorkerReply>(text), child.ExitCode);
            }
            finally
            {
                // A kill is cleanup only. It cannot undo a request accepted by Explorer.
                try
                {
                    if (!child.HasExited)
                    {
                        try { child.Kill(); }
                        catch (InvalidOperationException) when (child.HasExited) { }
                        using var cleanup = new CancellationTokenSource(1_000);
                        await child.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await deadline.CancelAsync().ConfigureAwait(false);
                    try { await Task.WhenAll(output, errors).ConfigureAwait(false); }
                    catch (Exception e) when (e is not OutOfMemoryException) { }
                }
            }
        });

    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder(); var buffer = new char[1_024];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (text.Length + count > MaximumCharacters) throw new InvalidDataException("Worker output oversized.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
