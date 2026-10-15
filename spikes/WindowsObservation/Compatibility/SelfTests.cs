using WindowsObservation;

namespace DesktopCompatibility;

internal static class SelfTests
{
    internal static int Run()
    {
        var passed = 0;
        var failed = 0;
        void Test(string name, Action body)
        {
            try { body(); passed++; Console.WriteLine("PASS: " + name); }
            catch { failed++; Console.WriteLine("FAIL: " + name); }
        }
        static void Check(bool condition) { if (!condition) throw new InvalidOperationException(); }
        static void Expect(string reason, Action action)
        {
            try { action(); }
            catch (DiagnosticFailure error) when (error.Category == reason) { return; }
            throw new InvalidOperationException();
        }
        Test("diagnostic exact build, architecture, privilege, release and consent gates", () =>
        {
            Check(Policy.Allows("26200.9711", "X64", "X64", "25H2", false, true, true));
            foreach (var build in new[] { "26200.9457", "26200.9712", "26200", "", "26100.9711" })
                Check(!Policy.Allows(build, "X64", "X64", "25H2", false, true, true));
            foreach (var architecture in new[] { "Arm64", "X86", "x64", "" })
            {
                Check(!Policy.Allows("26200.9711", architecture, "X64", "25H2", false, true, true));
                Check(!Policy.Allows("26200.9711", "X64", architecture, "25H2", false, true, true));
            }
            Check(!Policy.Allows("26200.9711", "X64", "X64", "24H2", false, true, true));
            Check(!Policy.Allows("26200.9711", "X64", "X64", "25H2", true, true, true));
            Check(!Policy.Allows("26200.9711", "X64", "X64", "25H2", false, false, true));
            Check(!Policy.Allows("26200.9711", "X64", "X64", "25H2", false, true, false));
        });
        Test("shared read-only policy accepts exactly the owner-approved builds", () =>
        {
            foreach (var build in new[] { "26200.9457", "26200.9711" })
            {
                Check(DesktopObservationPolicy.IsSupported(build, "X64"));
                foreach (var architecture in new[] { "Arm64", "X86", "x64", "" })
                    Check(!DesktopObservationPolicy.IsSupported(build, architecture));
            }
            foreach (var build in new[] { "26200.9456", "26200.9458", "26200.9710", "26200.9712", "26200", "" })
                Check(!DesktopObservationPolicy.IsSupported(build, "X64"));
        });
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        DesktopReading Good() => new(Capability.Available, a, [a, b], 1);
        Test("invalid readings fail closed", () =>
        {
            Check(Policy.Valid(Good()));
            foreach (var bad in new[] { Good() with { Health = Capability.Unavailable }, Good() with { Current = null },
                Good() with { Current = Guid.Empty }, Good() with { Current = Guid.NewGuid() },
                Good() with { Desktops = [] }, Good() with { Desktops = [a, a] }, Good() with { Desktops = [a, Guid.Empty] },
                Good() with { ShellProcess = 0 }, Good() with { Desktops = new Guid[65] } })
                Check(!Policy.Valid(bad));
            Check(!Policy.Valid(null));
        });
        Test("shell/topology changes invalidate; reordering does not rebind", () =>
        {
            var check = new EndpointCheck(); check.Accept(Good());
            check.Accept(Good() with { Current = b, Desktops = [b, a] });
            Expect("ShellGenerationChanged", () => check.Accept(Good() with { ShellProcess = 2 }));
            Expect("TopologyChanged", () => check.Accept(Good() with { Desktops = [a] }));
        });
        Test("21 alternating endpoint confirmations yield 20 transitions", () =>
        {
            var gate = new UserObservationGate(true);
            for (var i = 0; i < 21; i++) Check(gate.Confirm(true, true, true, false, true, i % 2 == 0 ? a : b, [a, b]));
            Check(gate.Complete && gate.Confirmations == 21);
        });
        Test("wrong/same endpoint and missing binding are terminal", () =>
        {
            var gate = new UserObservationGate(true);
            Check(gate.Confirm(true, true, true, false, true, a, [a, b]));
            Check(!gate.Confirm(true, true, true, false, true, a, [a, b]));
            Check(gate.Failure == "DesktopDidNotChange");
            gate = new(true);
            Check(gate.Confirm(true, true, true, false, true, a, [a, b]));
            Check(gate.Confirm(true, true, true, false, true, b, [a, b]));
            Check(!gate.Confirm(true, true, true, false, true, a, [a]));
        });
        Test("cleanup timeout remains explicit", () =>
        {
            var threw = false;
            try { WorkerShutdown.Stop(() => false, () => { }, () => false); }
            catch (TimeoutException) { threw = true; }
            Check(threw);
        });
        Test("valid private IPC read and normal cleanup", () =>
        {
            var worker = new OwnedWorker("--worker-good");
            try { Check(Policy.Valid(worker.Read())); }
            finally { worker.Dispose(); }
            Check(worker.CleanupConfirmed);
        });
        foreach (var (mode, reason) in new[] { ("hang", "WorkerTimeout"), ("exit", "WorkerExit"),
            ("malformed", "WorkerProtocolOrExit"), ("oversized", "WorkerOutputLimit"),
            ("invalid", "InvalidReading"), ("com-error", "ComFailure") })
        {
            Test("non-COM owned fault / terminal state / exit confirmed: " + mode, () =>
            {
                var worker = new OwnedWorker("--worker-" + mode);
                try
                {
                    Expect(reason, () => worker.Read(mode == "hang" ? 500 : 2_000));
                    Expect("WorkerTerminal", () => worker.Read());
                }
                finally { worker.Dispose(); }
                Check(worker.CleanupConfirmed);
            });
        }
        Program.Write(new { result = failed == 0 ? "PASS" : "FAIL", passed, failed, evidence = "SyntheticAndOwnedProcesses_NoCom" });
        return failed == 0 ? 0 : 1;
    }

    internal static int FaultWorker(string mode)
    {
        if (Console.ReadLine() != "read") return 2;
        switch (mode)
        {
            case "--worker-hang": Thread.Sleep(10_000); return 0;
            case "--worker-exit": return 1;
            case "--worker-malformed": Console.WriteLine("{broken}"); return 0;
            case "--worker-oversized": Console.WriteLine(new string('x', 4_098)); return 0;
            case "--worker-invalid": Program.Write(new Reply(new(Capability.Available, Guid.Empty, [], 0), null)); return 0;
            case "--worker-com-error": Program.Write(new Reply(null, "ComFailure")); return 0;
            case "--worker-good":
                var id = Guid.NewGuid();
                Program.Write(new Reply(new(Capability.Available, id, [id], 1), null));
                return 0;
            default: return 2;
        }
    }
}
