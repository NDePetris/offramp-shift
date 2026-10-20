using WindowsObservation;

namespace DesktopCompatibility;

internal static class Policy
{
    internal const string OptIn = "--confirm-experimental-read-only-26200.9711";
    internal const string OptIn26300 = "--confirm-experimental-read-only-26300.9711";
    internal static bool MatchesTarget(string optIn, string build) =>
        optIn == OptIn && build == "26200.9711" || optIn == OptIn26300 && build == "26300.9711";
    internal static bool Allows(string build, string processArchitecture, string osArchitecture,
        string release, bool elevated, bool interactive, bool consent) =>
        consent && ((build == "26200.9711" && release == "25H2") || (build == "26300.9711" && release == "26H2")) &&
        processArchitecture == "X64" && osArchitecture == "X64" && !elevated && interactive;

    internal static bool Valid(DesktopReading? reading) => reading is
    { Health: Capability.Available, Current: { } current, Desktops: { Length: >= 1 and <= 64 } ids, ShellProcess: > 0 } &&
        current != Guid.Empty && ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Length && ids.Contains(current);
}

internal sealed class EndpointCheck
{
    private uint? shell;
    private HashSet<Guid>? topology;
    internal void Accept(DesktopReading reading, Guid? expectedCurrent = null)
    {
        if (!Policy.Valid(reading)) throw new DiagnosticFailure("InvalidReading");
        if (expectedCurrent is { } expected && (expected == Guid.Empty || reading.Current != expected))
            throw new DiagnosticFailure("ExpectedCurrentDesktopMismatch");
        if (shell is not null && shell != reading.ShellProcess) throw new DiagnosticFailure("ShellGenerationChanged");
        if (topology is not null && !topology.SetEquals(reading.Desktops)) throw new DiagnosticFailure("TopologyChanged");
        shell = reading.ShellProcess;
        topology ??= reading.Desktops.ToHashSet();
    }
}

internal sealed class DiagnosticFailure(string category) : Exception
{
    internal string Category { get; } = category;
}
