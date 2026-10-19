using DesktopFeasibility;
using static Program;

internal static class SwitchWorkerGuardRegressions
{
    internal static void Run()
    {
        var personal = Guid.NewGuid(); var work = Guid.NewGuid();
        Snapshot Baseline(Configuration configuration) => new(configuration, Capabilities.Provider,
            DateTimeOffset.UtcNow, Environment.TickCount64, personal, [new(personal, ""), new(work, "")], 10, 20, true, null, null);
        AuthorizedSwitch Grant(Configuration configuration) => new(new(Guid.NewGuid(), Baseline(configuration), work, true), personal, work);
        void Entry(SwitchWorkerRequest request, Configuration configuration, bool qualification, bool sta, bool allowed)
        {
            var activations = 0;
            using var output = new StringWriter();
            var exit = SwitchWorkerEntry.RunRequest(new StringReader(Wire.Encode(request)), output, qualification, sta,
                () => configuration, () => { activations++; throw new InvalidOperationException("Fake activation; never COM."); }, _ => { });
            Check(activations == (allowed ? 1 : 0));
            Check(exit is 0 or 1);
            Check(SwitchProtocol.Decode<SwitchWorkerReply>(output.ToString()).Result?.Attempted != true);
        }
        foreach (var qualification in new[] { false, true })
            foreach (var architecture in new[] { "X64", "X86", "Arm64" })
                foreach (var build in new[] { "26200.9457", "26200.9711", "26200.9712" })
                    Test($"worker pre-activation exact-build guard: qualification={qualification} {build}/{architecture}", () =>
                    {
                        var configuration = new Configuration(build, architecture, 1, 2);
                        var allowed = architecture == "X64" && build == (qualification ? "26200.9711" : "26200.9457");
                        Entry(new("switch", Grant(configuration)), configuration, qualification, true, allowed);
                        Entry(new("capture"), configuration, qualification, true, qualification && allowed);
                    });
        Test("worker rejects unauthorized operations leases and apartment before activation", () =>
        {
            var configuration = new Configuration("26200.9457", "X64", 1, 2); var grant = Grant(configuration);
            Entry(new("switch", grant), configuration, false, false, false);
            foreach (var command in new[] { "capture", "unknown", "", "--switch-qualification-worker" })
                Entry(new(command), configuration, false, true, false);
            Entry(new("switch"), configuration, false, true, false);
            foreach (var invalid in new[] {
                grant with { Personal = Guid.Empty }, grant with { Work = personal },
                grant with { Request = grant.Request with { Confirmed = false } },
                grant with { Request = grant.Request with { RequestId = Guid.Empty } },
                grant with { Request = grant.Request with { Target = personal } },
                grant with { Request = grant.Request with { Expected = grant.Request.Expected with { ObservedUptimeMs = Environment.TickCount64 - 10_000 } } } })
                Entry(new("switch", invalid), configuration, false, true, false);
            Entry(new("switch", grant), configuration with { SessionId = 2 }, false, true, false);
            var diagnostic = configuration with { Build = "26200.9711" };
            Entry(new("capture", Grant(diagnostic)), diagnostic, true, true, false);
        });
    }
}
