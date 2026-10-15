using OfframpShift.ActivityMonitoring;
using OfframpShift.Core;
using OfframpShift.Persistence;
using WindowsObservation;

internal static class DurableEstimatorRegression
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("durable rejected delivery requires fresh evidence without losing saved Personal usage", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "offramp-delivery-" + Guid.NewGuid().ToString("N"));
            var epoch = Guid.NewGuid();
            var clock = new FakeClock { Now = new(epoch, 0) };
            var personal = Guid.NewGuid(); var work = Guid.NewGuid();
            var config = new MonitoringConfiguration { Accounting = AccountingMode.BestEffort };
            var health = new ProviderHealth(true, 1);
            try
            {
                using (var session = DurableMonitoringSession.Open(clock, config, path))
                {
                    var estimator = new IntervalEstimator(personal, work, config);
                    void Sample(long ms, Guid desktop, long delay = 0)
                    {
                        clock.Now = new(epoch, ms);
                        estimator.Point(new(clock.Now, ms, (ulong)ms * 10_000,
                            1, 1, new("opaque", true), 0, SessionState.Active,
                            new(Capability.Available, desktop, [personal, work], 1),
                            Capability.Available, Capability.Available, Capability.Available), health);
                        var batch = estimator.Finalize(clock.Now, health);
                        clock.Now = new(epoch, ms + delay);
                        foreach (var observation in batch)
                            if (!estimator.AcceptDelivery(observation, session.Observe(observation))) break;
                    }
                    for (var ms = 0; ms <= 5_000; ms += 1_000) Sample(ms, personal);
                    check(session.State.UsageMs == 4_000);
                    for (var ms = 6_000; ms <= 10_000; ms += 1_000) Sample(ms, work);
                    check(session.State.BreakProgressMs == 3_000);
                    // The final pending Personal second was accepted before Work's boundary.
                    check(session.State.UsageMs == 5_000);
                    Sample(11_000, work, 3_000);
                    check(session.State.BreakProgressMs == 0 && estimator.CoreRejectedIntervals == 1);
                    for (var ms = 16_000; ms <= 20_000; ms += 1_000) Sample(ms, work);
                    check(session.State.BreakProgressMs == 3_000 && session.State.UsageMs == 5_000);
                    check(session.State.EstimatedUsageMs == 5_000 && session.State.EffectSequence == 0);
                }
                clock.Now = new(Guid.NewGuid(), 0);
                using var restored = DurableMonitoringSession.Open(clock, config, path);
                check(restored.State.UsageMs == 5_000 && restored.State.EstimatedUsageMs == 5_000);
                check(restored.State.BreakProgressMs == 0 && restored.State.EffectSequence == 0);
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        });
        test("estimated intervals integrate with real durable storage and restart without replay", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "offramp-estimator-" + Guid.NewGuid().ToString("N"));
            var clock = new FakeClock { Now = new(Guid.NewGuid(), 0) };
            var personal = Guid.NewGuid(); var work = Guid.NewGuid();
            var config = new MonitoringConfiguration { Accounting = AccountingMode.BestEffort };
            var health = new ProviderHealth(true, 1);
            try
            {
                using (var session = DurableMonitoringSession.Open(clock, config, path))
                {
                    var estimator = new IntervalEstimator(personal, work, config);
                    for (var i = 0; i <= 10; i++)
                    {
                        clock.Now = clock.Now with { Milliseconds = i * 1_000 };
                        var point = new EvidencePoint(clock.Now, i * 1_000, (ulong)i * 10_000_000,
                            1, 1, new("opaque", true), 0, SessionState.Active,
                            new(Capability.Available, personal, [personal, work], 1), Capability.Available, Capability.Available, Capability.Available);
                        estimator.Point(point, health);
                        foreach (var observation in estimator.Finalize(clock.Now, health)) session.Observe(observation);
                    }
                    check(session.State.UsageMs == 9_000 && session.State.EstimatedUsageMs == 9_000);
                    check(session.State.EffectSequence == 0);
                }
                clock.Now = new(Guid.NewGuid(), 0);
                using var restored = DurableMonitoringSession.Open(clock, config, path);
                check(restored.State.UsageMs == 9_000 && restored.State.EstimatedUsageMs == 9_000);
                check(restored.State.EffectSequence == 0 && restored.State.BreakProgressMs == 0);
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        });
    }
}
