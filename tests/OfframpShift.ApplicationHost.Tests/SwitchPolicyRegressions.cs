using DesktopFeasibility;
using static Program;

internal static class SwitchPolicyRegressions
{
    internal static void Run()
    {
        var personal = Guid.NewGuid(); var work = Guid.NewGuid(); var other = Guid.NewGuid();
        var baseline = new Snapshot(new("26200.9457", "X64", 1, 2), Capabilities.Provider,
            DateTimeOffset.UtcNow, Environment.TickCount64, personal, [new(personal, ""), new(work, ""), new(other, "")], 10, 20, true, null, null);
        var request = new AuthorizedSwitch(new(Guid.NewGuid(), baseline, work, true), personal, work);
        Test("application and diagnostic compatibility remain distinct exact-build x64 gates", () =>
        {
            foreach (var architecture in new[] { "X64", "X86", "Arm64", "x64", "" })
                foreach (var build in new[] { "26200.9457", "26200.9711", "26200.9712", "26200", "26100.1", "26300.9711", "26300.9710", "26300.9712", "26300", "26400.9711", "" })
                {
                    Check(SwitchCompatibility.Application(build, architecture) == (build == "26200.9457" && architecture == "X64"));
                    Check(SwitchCompatibility.Qualification(build, architecture) == (build is "26200.9711" or "26300.9711" && architecture == "X64"));
                    Check(Capabilities.MayProbe(new(build, architecture, 1, 2)) == SwitchCompatibility.Application(build, architecture));
                }
        });
        Test("normal executor rejects newer build even with a valid single-use grant", () =>
        {
            foreach (var build in new[] { "26200.9711", "26300.9711", "26300.9712" })
            {
                var newer = request with { Request = request.Request with { Expected = baseline with { Configuration = baseline.Configuration with { Build = build } } } };
                var result = new IsolatedSwitchExecutor().ExecuteAsync(new(newer, Environment.TickCount64 + 2_000)).GetAwaiter().GetResult();
                Check(result.Outcome == SwitchOutcome.Rejected);
            }
        });
        Test("26300 qualification uses the existing single-call policy with a fake session", () =>
        {
            var snapshot = baseline with { Configuration = baseline.Configuration with { Build = "26300.9711" } };
            var diagnostic = request with { Request = request.Request with { Expected = snapshot } };
            var fake = new Session(snapshot, work);
            long time = 0;
            var result = SwitchExperiment.Run(diagnostic.Request, fake, () => snapshot.ObservedUtc.AddMilliseconds(time),
                () => snapshot.ObservedUptimeMs + time, () => time, n => time += n, qualification: true);
            Check(fake.Switches == 1 && result.Attempted);
            Check(SwitchProtocol.Classify(diagnostic, new(result), 0).Outcome == SwitchOutcome.ConfirmedSuccess);
        });
        Test("typed protocol rejects inconsistent and forged completion claims", () =>
        {
            var verified = new SwitchResult(request.Request.RequestId, "Verified", work, true, 30);
            SwitchCompletion Classify(SwitchResult result) => SwitchProtocol.Classify(request, new(result), 0);
            Check(Classify(verified).Outcome == SwitchOutcome.ConfirmedSuccess);
            Check(Classify(verified with { Status = "Rejected: Foreground changed.", Attempted = false, Observed = null }).Outcome == SwitchOutcome.Rejected);
            Check(Classify(verified with { Status = "Rejected: Foreground changed.", Attempted = false, Observed = null }).Message.Contains("Foreground changed.", StringComparison.Ordinal));
            Check(!Classify(verified with { Status = "Rejected: untrusted native text", Attempted = false, Observed = null }).Message.Contains("untrusted native text", StringComparison.Ordinal));
            Check(Classify(verified with { Status = "Rejected: untrusted native text", Attempted = false, Observed = null }).Outcome == SwitchOutcome.Indeterminate);
            var unexpected = Classify(verified with { Status = "WrongDestinationOrUserRace", Observed = other });
            Check(unexpected.Outcome == SwitchOutcome.Indeterminate);
            Check(unexpected.Detail.Contains("WrongDestinationOrUserRace", StringComparison.Ordinal));
            Check(unexpected.Message.Contains("Win+Tab", StringComparison.Ordinal));
            foreach (var invalid in new[] { verified with { RequestId = Guid.NewGuid() }, verified with { Observed = personal },
                verified with { Attempted = false }, verified with { ElapsedMs = -1 }, verified with { ElapsedMs = 3_001 },
                verified with { Status = "Unknown" }, verified with { Status = "Rejected: error" },
                verified with { Status = "WrongDestinationOrUserRace", Observed = Guid.NewGuid() },
                verified with { Status = "Indeterminate: verification timeout", Observed = null } })
                Check(Classify(invalid).Outcome == SwitchOutcome.Indeterminate);
            Check(SwitchProtocol.Classify(request, new(verified), 23).Outcome == SwitchOutcome.Indeterminate);
            Check(SwitchProtocol.Classify(request, new(verified, Snapshot: baseline), 0).Outcome == SwitchOutcome.Indeterminate);
            Check(SwitchProtocol.Classify(request, new(Error: "failure"), 0).Outcome == SwitchOutcome.Indeterminate);
        });
        Test("IPC rejects duplicate nested properties unknown fields malformed and missing replies", () =>
        {
            foreach (var text in new[] { "{", "null", "{\"result\":null,\"result\":null}", "{\"extra\":1}",
                "{\"result\":{\"requestId\":\"00000000-0000-0000-0000-000000000000\",\"requestId\":\"00000000-0000-0000-0000-000000000000\"}}" })
            {
                var rejected = false;
                try { SwitchProtocol.Decode<SwitchWorkerReply>(text); } catch (Exception) { rejected = true; }
                Check(rejected);
            }
            Check(SwitchProtocol.Classify(request, SwitchProtocol.Decode<SwitchWorkerReply>("{}"), 0).Outcome == SwitchOutcome.Indeterminate);
        });
        foreach (var phase in new[] { "before", "resolved", "after" })
            foreach (var change in new[] { "session", "shell", "topology", "foreground", "source", "interactive" })
                Test($"native policy checks {change} {phase} with at most one shell call", () =>
                {
                    Snapshot Change(Snapshot value) => change switch
                    {
                        "session" => value with { Configuration = value.Configuration with { SessionId = 2 } },
                        "shell" => value with { ShellProcessId = 21 },
                        "topology" => value with { Desktops = value.Desktops.Reverse().ToArray() },
                        "foreground" => value with { ForegroundWindow = 11 },
                        "source" => value with { Current = other },
                        _ => value with { Interactive = false }
                    };
                    var fake = new Session(baseline, work) { Change = Change, Phase = phase };
                    long time = 0;
                    var result = SwitchExperiment.Run(request.Request, fake, () => baseline.ObservedUtc.AddMilliseconds(time),
                        () => baseline.ObservedUptimeMs + time, () => time, n => time += n);
                    Check(fake.Switches == (phase == "after" ? 1 : 0));
                    if (phase != "after") Check(!result.Attempted && result.Status.StartsWith("Rejected:", StringComparison.Ordinal));
                    else if (change is "session" or "shell" or "topology" or "interactive") Check(result.Status.StartsWith("Indeterminate:", StringComparison.Ordinal));
                });
        Test("single-use lease cannot be raced into overlapping execution", () =>
        {
            var grant = new SwitchAuthorization(request, Environment.TickCount64 + 2_000); var accepted = 0;
            Parallel.For(0, 100, _ => { if (grant.Consume(Environment.TickCount64) is not null) Interlocked.Increment(ref accepted); });
            Check(accepted == 1);
        });
    }
    private sealed class Session(Snapshot baseline, Guid target) : IDesktopSession
    {
        internal int Switches;
        internal string Phase = "";
        internal Func<Snapshot, Snapshot> Change = x => x;
        private int observations;
        public Snapshot Observe()
        {
            observations++;
            var value = Switches == 0 ? baseline : baseline with { Current = target };
            return Phase == "before" || Phase == "resolved" && observations == 2 || Phase == "after" && Switches > 0 ? Change(value) : value;
        }
        public Guid ResolveIdentity(Guid id) => id;
        public void SwitchByIdentity(Guid id) { Check(id == target); Switches++; }
    }
}
