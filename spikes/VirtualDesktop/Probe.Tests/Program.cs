using DesktopFeasibility;

var now = DateTimeOffset.UtcNow;
var personal = Guid.NewGuid();
var work = Guid.NewGuid();
var other = Guid.NewGuid();
var configuration = new Configuration("26200.9457", "X64", 1, 2);
var baseline = new Snapshot(configuration, Capabilities.Provider, now, 10000, personal,
    [new(personal, "Personal"), new(work, "Work")], 100, 200, true, personal, true);
var bindings = new Bindings(1, baseline.Provider, configuration.Build, personal, work);
var request = new SwitchRequest(Guid.NewGuid(), baseline, work, true);
var passed = 0;
var failed = 0;

Test("exact build allowed", () => Equal(true, Capabilities.MayProbe(configuration)));
Test("different revision denied", () => Equal(false, Capabilities.MayProbe(configuration with { Build = "26200.9458" })));
Test("different build denied", () => Equal(false, Capabilities.MayProbe(configuration with { Build = "26100.2605" })));
Test("ARM64 denied", () => Equal(false, Capabilities.MayProbe(configuration with { Architecture = "Arm64" })));
Test("valid observation", () => Safety.ValidateSnapshot(baseline));
Test("empty topology", () => Throws(() => Safety.ValidateSnapshot(baseline with { Desktops = [] })));
Test("empty identity", () => Throws(() => Safety.ValidateSnapshot(baseline with { Desktops = [new(Guid.Empty, "")] })));
Test("duplicate identity", () => Throws(() => Safety.ValidateSnapshot(baseline with { Desktops = [new(personal, "A"), new(personal, "B")] })));
Test("unknown active identity", () => Throws(() => Safety.ValidateSnapshot(baseline with { Current = other })));
Test("no shell", () => Throws(() => Safety.ValidateSnapshot(baseline with { ShellProcessId = 0 })));
Test("no monitor", () => Throws(() => Safety.ValidateSnapshot(baseline with { Configuration = configuration with { MonitorCount = 0 } })));
Test("binding round trip", () => Equal(bindings, Wire.Decode<Bindings>(Wire.Encode(bindings))));
Test("valid persisted binding", () => Safety.ValidateBindings(bindings, baseline, true));
Test("renaming preserves bindings", () => Safety.ValidateBindings(bindings, baseline with { Desktops = [new(personal, "Renamed"), new(work, "Also renamed")] }, true));
Test("reordering preserves bindings", () => Safety.ValidateBindings(bindings, baseline with { Desktops = baseline.Desktops.Reverse().ToArray() }, true));
Test("deletion rejects binding", () => Throws(() => Safety.ValidateBindings(bindings, baseline with { Desktops = [new(personal, "Personal")] }, true)));
Test("same name never substitutes deleted GUID", () => Throws(() => Safety.ValidateBindings(bindings, baseline with { Desktops = [new(personal, "Personal"), new(other, "Work")] }, true)));
Test("same roles denied", () => Throws(() => Safety.ValidateBindings(bindings with { Work = personal }, baseline, true)));
Test("partial binding permitted for onboarding", () => Safety.ValidateBindings(bindings with { Work = null }, baseline, false));
Test("partial binding denied for switch", () => Throws(() => Safety.ValidateBindings(bindings with { Work = null }, baseline, true)));
Test("binding version denied", () => Throws(() => Safety.ValidateBindings(bindings with { Version = 2 }, baseline, true)));
Test("binding provider denied", () => Throws(() => Safety.ValidateBindings(bindings with { Provider = "unknown" }, baseline, true)));
Test("binding build requires revalidation", () => Throws(() => Safety.ValidateBindings(bindings with { Build = "26200.1" }, baseline, true)));
Test("target resolves role by identity", () => Equal(work, Safety.Target(bindings, baseline, "Work")));
Test("wrong source denied", () => Throws(() => Safety.Target(bindings, baseline with { Current = work }, "Work")));
Test("unbound source denied", () => Throws(() => Safety.Target(bindings, baseline with { Current = other, Desktops = [.. baseline.Desktops, new(other, "Other")] }, "Work")));
Test("no confirmation", () => Rejected(request with { Confirmed = false }, baseline));
Test("empty request identity", () => Rejected(request with { RequestId = Guid.Empty }, baseline));
Test("stale request", () => Rejected(request with { Expected = baseline with { ObservedUtc = now.AddSeconds(-3) } }, baseline));
Test("clock rollback rejects future timestamp", () => Rejected(request with { Expected = baseline with { ObservedUtc = now.AddSeconds(1) } }, baseline));
Test("monotonic lease expires even when UTC looks fresh", () => Rejected(request with { Expected = baseline with { ObservedUptimeMs = 7000 } }, baseline));
Test("monotonic counter discontinuity rejects", () => Rejected(request with { Expected = baseline with { ObservedUptimeMs = 11000 } }, baseline));
Test("user desktop change", () => Rejected(request, baseline with { Current = work }));
Test("focus change", () => Rejected(request, baseline with { ForegroundWindow = 101 }));
Test("Explorer restart", () => Rejected(request, baseline with { ShellProcessId = 201 }));
Test("locked session", () => Rejected(request, baseline with { Interactive = false }));
Test("session change", () => Rejected(request, baseline with { Configuration = configuration with { SessionId = 2 } }));
Test("monitor change", () => Rejected(request, baseline with { Configuration = configuration with { MonitorCount = 1 } }));
Test("reorder during request", () => Rejected(request, baseline with { Desktops = baseline.Desktops.Reverse().ToArray() }));
Test("creation during request", () => Rejected(request, baseline with { Desktops = [.. baseline.Desktops, new(other, "Other")] }));
Test("deletion during request", () => Rejected(request, baseline with { Desktops = [new(personal, "Personal")] }));
Test("target already current", () => Rejected(request with { Target = personal }, baseline));
Test("missing destination", () => Rejected(request with { Target = other }, baseline));
Test("pinned foreground membership does not determine active", () => Equal<string?>(null, Safety.Reject(request, baseline with { ForegroundDesktop = work }, now, 10000)));
Test("unknown JSON fields rejected", () => ThrowsAny(() => Wire.Decode<WorkerRequest>("{\"command\":\"snapshot\",\"extra\":true}")));
Test("malformed JSON rejected", () => ThrowsAny(() => Wire.Decode<WorkerRequest>("{")));
Test("null JSON rejected", () => ThrowsAny(() => Wire.Decode<WorkerRequest>("null")));

Test("successful switch exactly once", () =>
{
    var fake = new Fake(baseline, work);
    var result = Run(fake);
    Equal("Verified", result.Status); Equal(true, result.Attempted); Equal(1, fake.SwitchCalls); Equal(work, fake.LastTarget);
});
Test("resolved GUID mismatch never switches", () =>
{
    var fake = new Fake(baseline, work) { ResolveAs = other };
    var result = Run(fake);
    Equal(false, result.Attempted); Equal(0, fake.SwitchCalls);
});
Test("missing resolved object never switches", () =>
{
    var fake = new Fake(baseline, work) { FailResolve = true };
    Equal(false, Run(fake).Attempted); Equal(0, fake.SwitchCalls);
});
Test("focus changes after resolution never switches", () =>
{
    var fake = new Fake(baseline, work) { BeforeSecondObservation = s => s with { ForegroundWindow = 999 } };
    Equal(false, Run(fake).Attempted); Equal(0, fake.SwitchCalls);
});
Test("deletion after resolution never switches", () =>
{
    var fake = new Fake(baseline, work) { BeforeSecondObservation = s => s with { Desktops = [new(personal, "Personal")] } };
    Equal(false, Run(fake).Attempted); Equal(0, fake.SwitchCalls);
});
Test("switch exception never retries", () =>
{
    var fake = new Fake(baseline, work) { FailSwitch = true };
    Equal(true, Run(fake).Attempted); Equal(1, fake.SwitchCalls);
});
Test("wrong destination never corrected", () =>
{
    var fake = new Fake(baseline with { Desktops = [.. baseline.Desktops, new(other, "Other")] }, other);
    var result = Run(fake, request with { Expected = fake.Initial });
    Equal("WrongDestinationOrUserRace", result.Status); Equal(1, fake.SwitchCalls);
});
Test("verification timeout never retries", () =>
{
    var fake = new Fake(baseline, personal);
    var result = Run(fake);
    Equal("Indeterminate: verification timeout", result.Status); Equal(1, fake.SwitchCalls);
});
Test("verification failure never retries", () =>
{
    var fake = new Fake(baseline, work) { FailVerification = true };
    Equal(true, Run(fake).Attempted); Equal(1, fake.SwitchCalls);
});
Test("verification shell restart is indeterminate", () =>
{
    var fake = new Fake(baseline, work) { AfterSwitch = s => s with { ShellProcessId = 999 } };
    Equal("Indeterminate: verification environment changed", Run(fake).Status); Equal(1, fake.SwitchCalls);
});
Test("1,000 shuffled topologies cannot change GUID target", () =>
{
    var random = new Random(23);
    for (var i = 0; i < 1000; i++)
    {
        var entries = baseline.Desktops.OrderBy(_ => random.Next()).ToArray();
        var shuffled = baseline with { Desktops = entries };
        Equal(work, Safety.Target(bindings, shuffled, "Work"));
        var fake = new Fake(shuffled, work);
        Equal("Verified", Run(fake, request with { Expected = shuffled }).Status);
        Equal(work, fake.LastTarget);
    }
});

Console.WriteLine($"RESULT: {passed} passed, {failed} failed. Fake-provider tests are not Windows compatibility evidence.");
return failed == 0 ? 0 : 1;

void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
void Throws(Action body) { try { body(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
void ThrowsAny(Action body) { try { body(); } catch { return; } throw new Exception("Expected rejection"); }
void Rejected(SwitchRequest r, Snapshot s) { if (Safety.Reject(r, s, now, 10000) == null) throw new Exception("Expected rejection"); }
SwitchResult Run(Fake fake, SwitchRequest? r = null)
{
    long milliseconds = 0;
    return SwitchExperiment.Run(r ?? request, fake, () => now.AddMilliseconds(milliseconds), () => 10000 + milliseconds,
        () => milliseconds, n => milliseconds += n);
}

sealed class Fake(Snapshot initial, Guid after) : IDesktopSession
{
    public Snapshot Initial { get; } = initial;
    public Guid? ResolveAs { get; init; }
    public bool FailResolve { get; init; }
    public bool FailSwitch { get; init; }
    public bool FailVerification { get; init; }
    public Func<Snapshot, Snapshot>? BeforeSecondObservation { get; init; }
    public Func<Snapshot, Snapshot>? AfterSwitch { get; init; }
    public int SwitchCalls { get; private set; }
    public Guid LastTarget { get; private set; }
    private int observations;
    public Snapshot Observe()
    {
        observations++;
        if (SwitchCalls > 0)
        {
            if (FailVerification) throw new InvalidOperationException("verification failed");
            var next = Initial with { Current = after };
            return AfterSwitch?.Invoke(next) ?? next;
        }
        return observations >= 2 ? BeforeSecondObservation?.Invoke(Initial) ?? Initial : Initial;
    }
    public Guid ResolveIdentity(Guid target)
    {
        if (FailResolve) throw new InvalidOperationException("destination deleted");
        return ResolveAs ?? target;
    }
    public void SwitchByIdentity(Guid target)
    {
        SwitchCalls++; LastTarget = target;
        if (FailSwitch) throw new InvalidOperationException("COM failed");
    }
}
