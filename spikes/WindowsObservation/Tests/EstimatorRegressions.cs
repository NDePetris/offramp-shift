using OfframpShift.ActivityMonitoring;
using OfframpShift.Core;
using WindowsObservation;

internal static class EstimatorRegressions
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        var epoch = Guid.NewGuid(); var personal = Guid.NewGuid(); var work = Guid.NewGuid();
        var configuration = new MonitoringConfiguration { Accounting = AccountingMode.BestEffort, IdleThresholdMs = 2_000 };
        var health = new ProviderHealth(true, 1);
        EvidencePoint Point(long ms, Guid? desktop = null, long? lastInput = 0) => new(new(epoch, ms), ms,
            (ulong)ms * 10_000, 1, 1, new("opaque", true), lastInput is { } tick ? ms - tick : null,
            SessionState.Active, new(Capability.Available, desktop ?? personal, [personal, work], 1),
            Capability.Available, lastInput is not null ? Capability.Available : Capability.Unavailable, Capability.Available);
        IntervalEstimator Make() => new(personal, work, configuration);
        test("estimator holdback never charges an open or stale interval", () =>
        {
            var e = Make(); e.Point(Point(0), health); e.Point(Point(1_000), health);
            check(e.Finalize(new(epoch, 1_999), health).Count == 0);
            var valid = e.Finalize(new(epoch, 2_000), health);
            check(valid.Count == 1 && valid[0].Confidence == EvidenceConfidence.Estimated);
            e.Point(Point(2_000), health);
            check(e.Finalize(new(epoch, 5_001), health).Single().Confidence == EvidenceConfidence.Unknown);
        });
        test("already-idle startup counts only fresh Personal intervals across threshold and input return", () =>
        {
            var e = Make(); e.Point(Point(10_000), health);
            check(e.Finalize(new(epoch, 10_000), health).Count == 0);
            var clock = new FakeClock { Now = new(epoch, 10_000) };
            var engine = new MonitoringCoordinator(clock, configuration);
            for (var ms = 11_000; ms <= 20_000; ms += 1_000)
            {
                clock.Now = new(epoch, ms);
                e.Point(Point(ms, lastInput: ms >= 18_000 ? 17_500 : 0), health);
                foreach (var observation in e.Finalize(clock.Now, health))
                {
                    check(observation.Confidence == EvidenceConfidence.Estimated);
                    check(engine.Observe(observation).Activity == ActivityKind.CountedPersonal);
                }
            }
            check(engine.State.UsageMs == 9_000 && engine.State.BreakProgressMs == 0);
            check(e.UnknownMs == 0);
        });
        test("rapid foreground change and return cannot become matching-endpoint activity", () =>
        {
            var e = Make(); e.Point(Point(0), health);
            e.Signal(new(1, SignalKind.Foreground, new(epoch, 200), 200, 200, 2));
            e.Signal(new(2, SignalKind.Foreground, new(epoch, 400), 400, 400, 1));
            e.Point(Point(1_000), health);
            check(e.Finalize(new(epoch, 2_000), health).Single().Confidence == EvidenceConfidence.Unknown);
        });
        test("Work foreground changes retain qualifying scope while desktop excursions invalidate it", () =>
        {
            var e = Make(); e.Point(Point(0, work), health);
            e.Signal(new(1, SignalKind.Foreground, new(epoch, 200), 200, 200, 2));
            e.Point(Point(1_000, work), health);
            check(e.Finalize(new(epoch, 2_000), health).Single().Desktop == DesktopScope.Work);
            e.Signal(new(2, SignalKind.InputDesktopChanged, new(epoch, 2_100), 2_100, 2_100));
            e.Point(Point(2_200, work), health);
            check(e.Finalize(new(epoch, 3_200), health).Single().Confidence == EvidenceConfidence.Unknown);
        });
        test("provider generation loss invalidates pending evidence before commit", () =>
        {
            var e = Make(); e.Point(Point(0), health); e.Point(Point(1_000), health);
            var replaced = health with { Generation = 2 };
            e.Point(Point(2_000), replaced);
            check(e.Finalize(new(epoch, 2_000), replaced).Single().Confidence == EvidenceConfidence.Unknown);
        });
        test("returning input on Work does not falsely interrupt a qualifying break", () =>
        {
            var e = Make(); e.Point(Point(0, work), health);
            for (var i = 1; i <= 3; i++) { e.Point(Point(i * 1_000, work), health); e.Finalize(new(epoch, i * 1_000), health); }
            e.Point(Point(4_000, work, 3_500), health);
            e.Finalize(new(epoch, 4_000), health);
            check(e.Finalize(new(epoch, 5_000), health).Single().Desktop == DesktopScope.Work);
        });
        test("invalid last-input ticks cause hard uncertainty", () =>
        {
            var e = Make(); e.Point(Point(0), health);
            e.Point(Point(1_000) with { InputDiscontinuity = true, IdleAgeMs = null }, health);
            check(e.Finalize(new(epoch, 2_000), health).Single().Continuity == ContinuityDisposition.Invalidate);
        });
        test("temporary unavailable identity is soft but sleep and long gaps are hard", () =>
        {
            var e = Make(); e.Point(Point(0), health);
            e.Point(Point(1_000) with { Application = null, Foreground = Capability.Unavailable }, health);
            check(e.Finalize(new(epoch, 2_000), health).Single().Continuity == ContinuityDisposition.PreserveTransient);
            e.Point(Point(3_000), health);
            check(e.Finalize(new(epoch, 4_000), health).Single().Continuity == ContinuityDisposition.Invalidate);
            e.Point(Point(4_000) with { Awake100ns = 30_000_000 }, health);
            check(e.Finalize(new(epoch, 5_000), health).Single().Confidence == EvidenceConfidence.Unknown);
        });
        test("unknown desktop, changed identity and slow snapshot never charge", () =>
        {
            foreach (var point in new[]
            {
                Point(1_000) with { Desktop = new(Capability.Unavailable, null, [], 1) },
                Point(1_000) with { Desktop = new(Capability.Available, null, [personal, work], 1) },
                Point(1_000) with { Application = new("different", true) },
                Point(1_000) with { CollectionMs = 501 }
            })
            {
                var e = Make(); e.Point(Point(0), health); e.Point(point, health);
                check(e.Finalize(new(epoch, 2_000), health).Single().Confidence == EvidenceConfidence.Unknown);
            }
        });
        test("late events invalidate pending state without rewriting finalized totals", () =>
        {
            var e = Make(); e.Point(Point(0), health); e.Point(Point(1_000), health);
            e.Finalize(new(epoch, 2_000), health);
            e.Signal(new(1, SignalKind.Foreground, new(epoch, 2_500), 2_500, 500, 2));
            e.Point(Point(2_000), health);
            check(e.Finalize(new(epoch, 3_000), health).Single().Confidence == EvidenceConfidence.Unknown);
            check(e.EstimatedMs == 1_000 && e.LateContradictions == 1);
        });
        test("ambiguous native generation time invalidates all pending evidence", () =>
        {
            var e = Make(); e.Point(Point(0), health); e.Point(Point(1_000), health);
            e.Signal(new(1, SignalKind.Foreground, new(epoch, 1_100), 1_100, 2_000, 2));
            check(e.Finalize(new(epoch, 2_000), health).Single().Confidence == EvidenceConfidence.Unknown);
        });
        test("pending queue remains bounded and overflow becomes one unknown interval", () =>
        {
            var e = Make();
            for (var i = 0; i <= 30; i++) e.Point(Point(i * 1_000), health);
            var intervals = e.Finalize(new(epoch, 40_000), health);
            check(intervals.Count <= 6 && intervals.All(x => x.Confidence == EvidenceConfidence.Unknown));
        });
        test("suspend gap discards stale estimates and fresh Work accounting resumes", () =>
        {
            var clock = new FakeClock { Now = new(epoch, 0) };
            var engine = new MonitoringCoordinator(clock, configuration);
            var estimator = Make();
            void Sample(long ms)
            {
                clock.Now = new(epoch, ms);
                estimator.Point(Point(ms, work, ms), health);
                foreach (var observation in estimator.Finalize(clock.Now, health)) engine.Observe(observation);
            }
            for (var ms = 0; ms <= 5_000; ms += 1_000) Sample(ms);
            check(engine.State.BreakProgressMs == 4_000);
            // An old interval is pending when the host is suspended for two minutes.
            Sample(125_000);
            check(engine.State.BreakProgressMs == 0 && engine.State.UsageMs == 0);
            for (var ms = 126_000; ms <= 130_000; ms += 1_000) Sample(ms);
            check(engine.State.BreakProgressMs == 3_000);
            check(engine.State.EffectSequence == 0);
            check(estimator.EstimatedMs == 7_000);
            check(estimator.UnknownMs == 121_000);
        });
        test("Core receipt-time rejection cannot strand subsequent fresh Work estimates", () =>
        {
            foreach (var unknown in new[] { false, true })
            {
                var clock = new FakeClock { Now = new(epoch, 0) };
                var engine = new MonitoringCoordinator(clock, configuration);
                var estimator = Make();
                void Sample(long ms, long deliveryDelay = 0, bool unavailable = false)
                {
                    clock.Now = new(epoch, ms);
                    var currentHealth = health with { Healthy = !unavailable };
                    estimator.Point(Point(ms, work, ms), currentHealth);
                    var observations = estimator.Finalize(clock.Now, currentHealth);
                    // Delivery can be delayed after finalization, including by a prior Save.
                    clock.Now = new(epoch, ms + deliveryDelay);
                    foreach (var observation in observations)
                        if (!estimator.AcceptDelivery(observation, engine.Observe(observation))) break;
                }
                for (var ms = 0; ms <= 5_000; ms += 1_000) Sample(ms);
                check(engine.State.BreakProgressMs == 4_000);
                var delay = unknown ? 4_001 : 1_001;
                Sample(6_000, delay, unknown);
                check(engine.State.BreakProgressMs == 0);
                for (var ms = 7_000 + delay; ms <= 12_000 + delay; ms += 1_000) Sample(ms);
                check(engine.State.BreakProgressMs == 4_000);
                check(engine.State.UsageMs == 0 && engine.State.EffectSequence == 0);
                check(estimator.CoreRejectedIntervals == 1 && estimator.CoreAcceptedEstimatedMs == 8_000);
                check(estimator.CoreAcceptedUnknownMs == 0);
            }
        });
        test("power epoch changes recover after Core clock rejection without carrying partial credit", () =>
        {
            var clock = new FakeClock { Now = new(epoch, 0) };
            var engine = new MonitoringCoordinator(clock, configuration);
            var estimator = Make();
            void Sample(Guid currentEpoch, long ms)
            {
                clock.Now = new(currentEpoch, ms);
                estimator.Point(Point(ms, work, ms) with { Time = clock.Now }, health);
                foreach (var observation in estimator.Finalize(clock.Now, health))
                    if (!estimator.AcceptDelivery(observation, engine.Observe(observation))) break;
            }
            for (var ms = 0; ms <= 5_000; ms += 1_000) Sample(epoch, ms);
            check(engine.State.BreakProgressMs == 4_000);
            // Native power notifications cause the host clock to start a new epoch.
            estimator.Signal(new(1, SignalKind.Power, clock.Now, 5_000, null, Detail: 4));
            var resumedEpoch = Guid.NewGuid();
            for (var ms = 0; ms <= 2_000; ms += 1_000) Sample(resumedEpoch, ms);
            check(estimator.CoreRejectedIntervals == 1 && engine.State.BreakProgressMs == 0);
            for (var ms = 3_000; ms <= 7_000; ms += 1_000) Sample(resumedEpoch, ms);
            check(engine.State.BreakProgressMs == 3_000 && estimator.CoreAcceptedEstimatedMs == 7_000);
            check(engine.State.UsageMs == 0 && engine.State.EffectSequence == 0);
        });
        test("full default Work break resets once and returning Personal cannot erase its effect sequence", () =>
        {
            var full = new MonitoringConfiguration { Accounting = AccountingMode.BestEffort };
            var estimator = new IntervalEstimator(personal, work, full);
            var clock = new FakeClock { Now = new(epoch, 0) };
            var engine = new MonitoringCoordinator(clock, full);
            long resets = 0, sequence = 0;
            for (var second = 0; second <= 1_080; second++)
            {
                var ms = second * 1_000L;
                clock.Now = new(epoch, ms);
                var scope = second is >= 60 and < 1_020 ? work : personal;
                if (second is > 60 and < 1_020 && second % 20 == 0)
                    estimator.Signal(new(++sequence, SignalKind.Foreground, clock.Now, ms, (uint)ms, 2));
                // Work remains qualifying across active/qualified-idle input changes.
                var inputTick = scope == work ? 60_000 : ms;
                estimator.Point(Point(ms, scope, inputTick), health);
                foreach (var observation in estimator.Finalize(clock.Now, health))
                {
                    var result = engine.Observe(observation);
                    resets += result.Effects.Count(x => x.Kind == EffectKind.AllowanceReset);
                }
                if (second == 1_019)
                    check(engine.State.BreakCompleted && engine.State.UsageMs == 0 && resets == 1);
            }
            check(resets == 1 && engine.State.EffectSequence == 1);
            check(!engine.State.BreakCompleted && engine.State.BreakProgressMs == 0 && engine.State.UsageMs > 0);
        });
    }
}
