using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

namespace WindowsObservation;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 0) return 2; // A broken spawn guard must not recursively run tests.
        var cases = 0;
        var personal = Guid.NewGuid();
        var missing = unchecked((int)0x8002802B);
        if (WindowDesktopMembership.CheckCandidate(true, missing, Guid.Empty, personal, true, false) != NudgeDesktopCheck.Allowed ||
            WindowDesktopMembership.CheckCandidate(true, missing, Guid.Empty, personal, false, false) != NudgeDesktopCheck.CandidateIdentityUnavailable ||
            WindowDesktopMembership.CheckCandidate(true, missing, Guid.Empty, personal, true, true) != NudgeDesktopCheck.CandidateIdentityUnavailable ||
            WindowDesktopMembership.CheckCandidate(true, -1, Guid.Empty, personal, true, false) != NudgeDesktopCheck.CandidateIdentityUnavailable ||
            WindowDesktopMembership.CheckCandidate(false, missing, Guid.Empty, personal, true, false) != NudgeDesktopCheck.CandidateNotCurrent ||
            WindowDesktopMembership.CheckCandidate(true, 0, Guid.NewGuid(), personal, true, false) != NudgeDesktopCheck.CandidateWrongDesktop ||
            WindowDesktopMembership.CheckCandidate(true, 0, personal, personal, false, true) != NudgeDesktopCheck.Allowed || Native.Calls != 0) return 1;
        Console.WriteLine("PASS: hidden candidate preparation never grants strict activation; wrong/unknown desktop and other HRESULTs fail closed (pure decisions).");
        foreach (var build in new[] { "26200.9456", "26200.9458", "26200.9710", "26200.9712", "26100.9711", "26200", "26300.-1", "26300.+1", "26300", "26400.9711" })
        {
            Registry.Build = build;
            using var worker = new DesktopWorker();
            if (worker.OwnedProcess is not null || worker.Read().Health != Capability.Unsupported ||
                DesktopWorker.Run() != 2 || worker.CheckNudge(1, 2, Guid.NewGuid()) || Native.Calls != 0) return 1;
            cases++;
        }
        foreach (var build in new[] { "26200.9457", "26200.9711", "26300.9711", "26300.9712" })
        {
            if (!DesktopWorker.IsSupported(build, Architecture.X64)) return 1;
            foreach (var architecture in new[] { Architecture.X86, Architecture.Arm64, Architecture.Arm })
            {
                if (DesktopWorker.IsSupported(build, architecture)) return 1;
                Registry.Build = build;
                RuntimeInformation.ProcessArchitecture = architecture;
                using var worker = new DesktopWorker();
                if (worker.OwnedProcess is not null || worker.Read().Health != Capability.Unsupported ||
                    DesktopWorker.Run() != 2 || worker.CheckNudge(1, 2, Guid.NewGuid()) || Native.Calls != 0) return 1;
                cases++;
            }
        }
        RuntimeInformation.ProcessArchitecture = Architecture.X64;
        foreach (var os in new[] { Architecture.Arm64, Architecture.X86 })
        {
            RuntimeInformation.OSArchitecture = os;
            Registry.Build = "26300.9711";
            using var worker = new DesktopWorker();
            if (DesktopWorker.IsSupported(Registry.Build, Architecture.X64) || worker.OwnedProcess is not null ||
                DesktopWorker.Run() != 2 || Native.Calls != 0) return 1;
            cases++;
        }
        Console.WriteLine($"PASS: {cases} unsupported-build/process/OS-architecture constructor/worker-entry cases rejected without child or native activation; exact/family matrices passed (test-only registry/runtime/native stubs).");
        return 0;
    }
}

// Namespace-local replacements exist ONLY in this test executable. The actual worker
// source above is compiled unchanged; these stubs are never referenced by a live project.
internal static class Registry
{
    internal static string Build { get; set; } = "";
    internal static FakeKey LocalMachine { get; } = new();
    internal sealed class FakeKey : IDisposable
    {
        internal FakeKey OpenSubKey(string _) => this;
        internal object GetValue(string name) => name == "CurrentBuild" ? Build.Split('.')[0] : Build.Split('.').ElementAtOrDefault(1) ?? "";
        public void Dispose() { }
    }
}

internal static class Native
{
    internal static int Calls { get; private set; }
    internal static nint GetShellWindow() { Calls++; throw new InvalidOperationException("NativeActivationForbiddenInGuardTests"); }
    internal static nint GetForegroundWindow() { Calls++; throw new InvalidOperationException("NativeActivationForbiddenInGuardTests"); }
    internal static OfframpShift.Core.SessionState Session() { Calls++; throw new InvalidOperationException("NativeActivationForbiddenInGuardTests"); }
    internal static uint GetWindowThreadProcessId(nint _, out uint process)
    { Calls++; process = 0; throw new InvalidOperationException("NativeActivationForbiddenInGuardTests"); }
}

internal static class RuntimeInformation
{
    internal static Architecture ProcessArchitecture { get; set; } = Architecture.X64;
    internal static Architecture OSArchitecture { get; set; } = Architecture.X64;
}
