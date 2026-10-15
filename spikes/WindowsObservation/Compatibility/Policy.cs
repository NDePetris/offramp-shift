using WindowsObservation;

namespace DesktopCompatibility;

internal static class Policy
{
    internal const string OptIn = "--confirm-experimental-read-only-26200.9711";
    internal static bool Allows(string build, string processArchitecture, string osArchitecture,
        string release, bool elevated, bool interactive, bool consent) =>
        consent && build == "26200.9711" && processArchitecture == "X64" && osArchitecture == "X64" &&
        release == "25H2" && !elevated && interactive;

    internal static bool Valid(DesktopReading? reading) => reading is
    { Health: Capability.Available, Current: { } current, Desktops: { Length: >= 1 and <= 64 } ids, ShellProcess: > 0 } &&
        current != Guid.Empty && ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Length && ids.Contains(current);
}

internal sealed class EndpointCheck
{
    private uint? shell;
    private HashSet<Guid>? topology;
    internal void Accept(DesktopReading reading)
    {
        if (!Policy.Valid(reading)) throw new DiagnosticFailure("InvalidReading");
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
