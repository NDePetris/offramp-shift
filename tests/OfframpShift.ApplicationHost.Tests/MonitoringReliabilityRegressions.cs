using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;
using System.Text.Json;
using WindowsObservation;
using static Program;

internal static class MonitoringReliabilityRegressions
{
    internal static void Run()
    {
        Test("queued heartbeat cannot renew provider health at processing time", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            f.Advance(app, 0);
            f.Clock.Now = f.Clock.Now with { Milliseconds = 1_000 }; f.Source!.Tick();
            f.Clock.Now = f.Clock.Now with { Milliseconds = 4_000 }; app.Step();
            Check(!app.Snapshot().Available);
        });
        foreach (var age in new[] { 2_000, 2_001 })
            Test("heartbeat receipt freshness boundary " + age, () =>
            {
                using var f = new Fixture(); f.Enable(); using var app = f.App();
                f.Advance(app, 0);
                f.Clock.Now = f.Clock.Now with { Milliseconds = 1_000 }; f.Source!.Tick();
                f.Clock.Now = f.Clock.Now with { Milliseconds = 1_000 + age }; app.Step();
                Check(app.Snapshot().Available == (age == 2_000));
                var usage = app.Snapshot().State!.UsageMs;
                f.Advance(app, 5_000); f.Advance(app, 6_000); f.Advance(app, 7_000); f.Advance(app, 8_000);
                Check(app.Snapshot().State!.UsageMs > usage);
            });
        foreach (var mismatch in new[] { "future", "epoch", "power" })
            Test("heartbeat cannot survive invalid receipt or power epoch: " + mismatch, () =>
            {
                using var f = new Fixture(); f.Enable(); using var app = f.App(); f.Advance(app, 0);
                f.Clock.Now = f.Clock.Now with { Milliseconds = 1_000 }; f.Source!.Tick();
                if (mismatch == "future") f.Clock.Now = f.Clock.Now with { Milliseconds = 999 };
                if (mismatch == "epoch") f.Clock.Now = new(Guid.NewGuid(), 1_000);
                if (mismatch == "power") f.Source.Emit(SignalKind.Power, detail: 7);
                app.Step(); Check(!app.Snapshot().Available);
                Check(app.Snapshot().State!.UsageMs == 0);
            });

        foreach (var delay in new[] { 499, 500, 501, 1_000, 1_001, 3_001 })
            Test("full controller slow successful save and fresh accounting resumption: " + delay, () =>
            {
                using var f = new Fixture(); f.Enable();
                using var start = new ManualResetEventSlim();
                MonitoringApplication? app = null; MonitoringController? controller = null;
                HostSnapshot? final = null; long afterCommitUsage = -1; var turn = 0; var delayed = false;
                var times = new long[] { 0, 1_000, 2_000, 3_000 + delay, 4_000 + delay, 5_000 + delay, 6_000 + delay, 7_000 + delay, 8_000 + delay };
                controller = new(() =>
                {
                    start.Wait(); app = f.App();
                    f.Fault = stage =>
                    {
                        if (stage != StorageFault.AfterCommit || delayed) return;
                        delayed = true; f.Clock.Now = f.Clock.Now with { Milliseconds = f.Ms + delay };
                    };
                    return app;
                }, () =>
                {
                    if (turn == 3) afterCommitUsage = app!.Snapshot().State!.UsageMs;
                    if (turn == times.Length)
                    { final = app!.Snapshot(); _ = controller!.StopAsync(); return; }
                    f.Clock.Now = f.Clock.Now with { Milliseconds = times[turn++] }; f.Source!.Tick();
                });
                start.Set();
                try { Check(controller.Completion.Wait(20_000)); }
                finally { Check(controller.StopAsync().Wait(20_000)); }
                Check(delayed && afterCommitUsage == 1_000);
                Check(final is { Persistence: PersistenceStatus.Ready, Available: true });
                Check(final!.State!.UsageMs > afterCommitUsage && final.State.BreakProgressMs == 0 && final.State.EffectSequence == 0);
                var all = JsonSerializer.SerializeToElement(controller.FinalDiagnostics);
                var monitoring = all.GetProperty("monitoring");
                Check(monitoring.GetProperty("MissedDeadlines").GetInt64() == delay / 1_000);
                Check(monitoring.GetProperty("accountingDelivery").GetProperty("CoreAcceptedEstimatedMs").GetInt64() == final.State.UsageMs);
                Check(monitoring.GetProperty("execution").GetProperty("observe").GetProperty("deadlineCrossings").GetInt64() == delay / 1_000);
                Check(all.GetProperty("owner").GetProperty("turns").GetProperty("latency").GetProperty("count").GetInt64() == times.Length);
                var phases = monitoring.GetProperty("commits").GetProperty("phases");
                var saves = phases.GetProperty("steadyState").GetProperty("count").GetInt64();
                Check(saves > 0 && phases.GetProperty("connectionSetup").GetProperty("count").GetInt64() == saves);
                Check(phases.GetProperty("commit").GetProperty("count").GetInt64() == saves);
                Check(phases.GetProperty("disposal").GetProperty("count").GetInt64() == 2 * saves);
                long unknown = 0;
                foreach (var item in monitoring.GetProperty("coverage").GetProperty("unknown").EnumerateObject()) unknown += item.Value.GetProperty("durationMs").GetInt64();
                Check(unknown == monitoring.GetProperty("UnknownMs").GetInt64());
                if (delay == 501) Check(monitoring.GetProperty("coverage").GetProperty("unknown").GetProperty("EndpointDiscontinuity").GetProperty("count").GetInt64() > 0);
                if (delay > 1_000) Check(monitoring.GetProperty("coverage").GetProperty("unknown").GetProperty("StaleGap").GetProperty("count").GetInt64() > 0);
                var json = all.GetRawText();
                Check(!json.Contains(f.Personal.ToString(), StringComparison.Ordinal) && !json.Contains("opaque-test", StringComparison.Ordinal) && !json.Contains(f.PathName, StringComparison.Ordinal));
                using var reopened = f.Session(); Check(reopened.State.UsageMs == final.State.UsageMs);
            });

        foreach (var delay in new[] { 800, 801, 1_000 })
            Test("full application receipt-age boundary after preceding Save: " + delay, () =>
            {
                using var f = new Fixture(); f.Enable(); using var app = f.App(); f.Source!.Current = f.Work;
                f.Advance(app, 0); f.Advance(app, 1_000);
                f.Clock.Now = f.Clock.Now with { Milliseconds = 1_100 }; f.Source.Emit(SignalKind.Foreground); app.Step();
                f.Advance(app, 1_200);
                f.Clock.Now = f.Clock.Now with { Milliseconds = 1_300 }; f.Source.Emit(SignalKind.Foreground); app.Step();
                f.Advance(app, 1_400);
                var delayed = false;
                f.Fault = stage =>
                {
                    if (stage != StorageFault.AfterCommit || delayed) return;
                    delayed = true; f.Clock.Now = f.Clock.Now with { Milliseconds = f.Ms + delay };
                };
                f.Advance(app, 2_400);
                var delivery = JsonSerializer.SerializeToElement(app.ObservationDiagnostics).GetProperty("accountingDelivery");
                Check(delayed && delivery.GetProperty("CoreRejectedIntervals").GetInt64() == (delay == 800 ? 0 : 1));
                if (delay > 800) Check(app.Snapshot().State!.BreakProgressMs == 0);
                for (var ms = 4_400; ms <= 8_400; ms += 1_000) f.Advance(app, ms);
                Check(app.Snapshot().State!.BreakProgressMs == 3_000 && app.Snapshot().State!.UsageMs == 0);
            });

        Test("heartbeat queued during successful Save stays stale until a new receipt", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            f.Advance(app, 0); f.Advance(app, 1_000);
            var delayed = false;
            f.Fault = stage =>
            {
                if (stage != StorageFault.AfterCommit || delayed) return;
                delayed = true; f.Source!.Tick(); f.Clock.Now = f.Clock.Now with { Milliseconds = f.Ms + 3_001 };
            };
            f.Advance(app, 2_000); app.Step();
            Check(delayed && !app.Snapshot().Available && app.Snapshot().State!.UsageMs == 1_000);
            var freshness = JsonSerializer.SerializeToElement(app.ObservationDiagnostics).GetProperty("freshness");
            Check(freshness.GetProperty("queueResidence").GetProperty("maximumUs").GetInt64() == 3_001_000);
            Check(freshness.GetProperty("staleHeartbeats").GetInt64() > 0);
            f.Advance(app, 6_001); f.Advance(app, 7_001); f.Advance(app, 8_001); f.Advance(app, 9_001);
            Check(app.Snapshot().Available && app.Snapshot().State!.UsageMs > 1_000);
        });
    }
}
