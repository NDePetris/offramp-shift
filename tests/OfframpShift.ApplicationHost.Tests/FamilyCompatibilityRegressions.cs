using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopFeasibility;
using OfframpShift.ApplicationHost;
using WindowsObservation;
using static Program;

internal static class FamilyCompatibilityRegressions
{
    internal static void Run()
    {
        Test("26300 family accepts numeric cumulative revisions only on x64 with independent capability policies", () =>
        {
            foreach (var build in new[] { "26300.0", "26300.1", "26300.9711", "26300.9712", "26300.10000", "26300.4294967295" })
            {
                Check(SwitchCompatibility.Is26300Family(build));
                foreach (var architecture in new[] { "X64", "X86", "Arm64", "x64", "" })
                {
                    Check(SwitchCompatibility.Application(build, architecture) == (architecture == "X64"));
                    Check(DesktopObservationPolicy.IsSupported(build, architecture) == (architecture == "X64"));
                    Check(!Capabilities.MayProbe(new(build, architecture, 1, 2)));
                }
            }
            foreach (var build in new[] { "26300", "26300.", "26300.-1", "26300.+1", "26300. 1", "26300.1 ",
                " 26300.1", "26300.1.0", "26300.4294967296", "263000.9711", "026300.9711", "26400.9711", "26100.1", "" })
            {
                Check(!SwitchCompatibility.Is26300Family(build));
                Check(!SwitchCompatibility.Application(build, "X64"));
                Check(!DesktopObservationPolicy.IsSupported(build, "X64"));
            }
            Check(DesktopObservationPolicy.IsSupported("26200.9711", "X64"));
            Check(!SwitchCompatibility.Application("26200.9711", "X64"));
            Check(!SwitchCompatibility.Qualification("26300.10000", "X64"));
            Check(DesktopObservationPolicy.SameConsentScope("26300.9711", "26300.10000"));
            Check(!DesktopObservationPolicy.SameConsentScope("26200.9711", "26300.9711"));
            Check(!DesktopObservationPolicy.SameConsentScope("26300.9711", "26400.9711"));
            foreach (var os in new[] { "X64", "Arm64", "X86", "Unknown" })
                foreach (var process in new[] { "X64", "Arm64", "X86", "Unknown" })
                    Check(SwitchCompatibility.Application("26300.9711", SwitchCompatibility.RuntimeArchitecture(process, os)) ==
                        (process == "X64" && os == "X64"));
        });
        Test("v4 exact switching consent migrates off without rewriting preferences or accounting", () =>
        {
            foreach (var build in new[] { "26200.9457", "26300.9711" })
            {
                using var f = new Fixture(); f.Enable(build: build);
                var original = f.Settings.Load() with { ReminderStyle = ReminderStyle.InteractiveWindow, Theme = AppTheme.Dark, CloseToTray = true };
                var node = JsonSerializer.SerializeToNode(original)!.AsObject();
                node["Version"] = 4; node.Remove("SwitchingConsentFamily");
                node["SwitchingConsent"] = true; node["SwitchingConsentBuild"] = build;
                var path = Path.Combine(f.PathName, "windows-settings.json"); File.WriteAllText(path, node.ToJsonString());
                var bytes = File.ReadAllBytes(path);
                var accounting = Directory.GetFiles(f.PathName).Where(p => p != path).ToDictionary(p => p, File.ReadAllBytes);
                var loaded = f.Settings.Load();
                Check(f.Settings.Status == SettingsStatus.Ready && loaded == original);
                Check(loaded.Version == 5 && !loaded.SwitchingConsent && loaded.SwitchingConsentBuild == "" && loaded.SwitchingConsentFamily == "");
                Check(bytes.SequenceEqual(File.ReadAllBytes(path)));
                foreach (var file in accounting) Check(file.Value.SequenceEqual(File.ReadAllBytes(file.Key)));
                f.Settings.Save(loaded); Check(f.Settings.Load() == loaded);
            }
        });
        Test("malformed legacy consent cannot be laundered by default-off migration", () =>
        {
            using var f = new Fixture(); f.Enable();
            var path = Path.Combine(f.PathName, "windows-settings.json");
            foreach (var mode in new[] { "empty", "mismatch", "unconfirmed", "null", "wrong-type", "extra" })
            {
                var node = JsonSerializer.SerializeToNode(f.Settings.Load())!.AsObject();
                node["Version"] = 4; node.Remove("SwitchingConsentFamily");
                node["SwitchingConsent"] = true; node["SwitchingConsentBuild"] = "26200.9457";
                switch (mode)
                {
                    case "empty": node["SwitchingConsentBuild"] = ""; break;
                    case "mismatch": node["SwitchingConsentBuild"] = "26300.9711"; break;
                    case "unconfirmed": node["SwitchingConsent"] = false; break;
                    case "null": node["SwitchingConsentBuild"] = null; break;
                    case "wrong-type": node["SwitchingConsent"] = "true"; break;
                    default: node["SwitchingConsentFamily"] = "26300"; break;
                }
                var text = node.ToJsonString(); File.WriteAllText(path, text);
                f.Settings.Load(); Check(f.Settings.Status == SettingsStatus.Invalid && File.ReadAllText(path) == text);
                File.Delete(path); f.Settings.Load(); f.Enable();
            }
        });
        Test("v5 family scope is default off and cannot masquerade as exact consent", () =>
        {
            Check(!new WindowsSettings().SwitchingConsent && new WindowsSettings().SwitchingConsentFamily == "");
            using var f = new Fixture(); f.Enable(build: "26300.9711");
            var approved = f.Settings.Load() with { SwitchingConsent = true, SwitchingConsentFamily = "26300" };
            approved.Validate(); Check(approved.HasSwitchingConsent("26300.9712"));
            foreach (var invalid in new[] { approved with { SwitchingConsentFamily = "26400" },
                approved with { SwitchingConsentBuild = "26300.9711" }, approved with { SwitchingConsentFamily = "", SwitchingConsentBuild = "26300.9711" },
                approved with { SwitchingConsent = false }, approved with { Build = "26400.9711" }, approved with { Consent = false } })
            {
                try { invalid.Validate(); Check(false); } catch (ArgumentException) { }
            }
        });
        Test("family switching requires a separate UI opt-in and fresh single-use grant without accounting mutation", () =>
        {
            using var f = new Fixture(); using var app = SwitchRegressions.Active(f, "26300.9712");
            Check(app.Execute(new(HostCommandKind.ChangeSwitchingConsent)).Succeeded);
            var before = app.Snapshot();
            Check(before.ObservationAllowed && !before.Settings.SwitchingConsent);
            Check(!app.Execute(new(HostCommandKind.AuthorizeSwitch) { NudgeWindow = 2 }).Succeeded);
            Check(app.Execute(new(HostCommandKind.ChangeSwitchingConsent) { Confirmed = true }).Succeeded);
            var saved = app.Snapshot().Settings;
            Check(saved.SwitchingConsent && saved.SwitchingConsentFamily == "26300" && saved.SwitchingConsentBuild == "");
            Check(app.Snapshot().State == before.State && app.Snapshot().Configuration == before.Configuration);
            // Preference commands revoke the old presentation, so a fresh nudge is
            // required. This also proves that opt-in alone cannot authorize a switch.
            Check(!app.Execute(new(HostCommandKind.AuthorizeSwitch) { NudgeWindow = 2 }).Succeeded);
        });
        Test("26300 application nudge authorizes once and disabling consent revokes an outstanding grant", () =>
        {
            using var f = new Fixture(); using var app = SwitchRegressions.Active(f, "26300.9711");
            var result = app.Execute(new(HostCommandKind.AuthorizeSwitch) { NudgeWindow = 2 });
            Check(result.Succeeded && result.Switching is not null);
            var request = result.Switching!.Consume(Environment.TickCount64);
            Check(request is { Valid: true } && request.Request.Expected.Configuration.Build == "26300.9711");
            Check(result.Switching.Consume(Environment.TickCount64) is null);
            var second = app.Execute(new(HostCommandKind.AuthorizeSwitch) { NudgeWindow = 2 });
            Check(second.Succeeded && second.Switching is not null);
            var before = app.Snapshot();
            Check(app.Execute(new(HostCommandKind.ChangeSwitchingConsent)).Succeeded);
            Check(second.Switching!.Consume(Environment.TickCount64) is null);
            Check(!app.Snapshot().Settings.SwitchingConsent && app.Snapshot().Settings.SwitchingConsentFamily == "");
            Check(app.Snapshot().State == before.State && app.Snapshot().Configuration == before.Configuration);
        });
        Test("routine UBR updates retain approved family consent and fresh observation setup", () =>
        {
            using var f = new Fixture();
            using (var app = SwitchRegressions.Active(f, "26300.9711")) { }
            var saved = f.Settings.Load();
            var bytes = File.ReadAllBytes(Path.Combine(f.PathName, "windows-settings.json"));
            using var updated = f.App(build: "26300.10000", switchConfiguration: () => new("26300.10000", "X64", 1, 2));
            Check(updated.Snapshot().ObservationAllowed && !updated.Snapshot().ConsentNeedsReview);
            Check(updated.Snapshot().Settings.HasSwitchingConsent("26300.10000"));
            for (var ms = 5_000; ms <= 8_000; ms += 1_000) f.Advance(updated, ms);
            Check(updated.Snapshot().BindingsValid && updated.Snapshot().Settings == saved);
            Check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(f.PathName, "windows-settings.json"))));
        });
        Test("major family change durably revokes consent and returning cannot resurrect it", () =>
        {
            using var f = new Fixture();
            using (var app = SwitchRegressions.Active(f, "26300.9711")) { }
            using (var unsupported = f.App(false, "26400.9711"))
                Check(!unsupported.Snapshot().ObservationAllowed && !unsupported.Snapshot().Settings.SwitchingConsent);
            Check(!f.Settings.Load().SwitchingConsent && f.Settings.Load().SwitchingConsentFamily == "");
            using var returned = f.App(build: "26300.9711");
            Check(!returned.Snapshot().Settings.HasSwitchingConsent("26300.9711"));
        });
        Test("family reassignment clears consent and a failed major-upgrade revocation blocks startup", () =>
        {
            using var f = new Fixture();
            using (var app = SwitchRegressions.Active(f, "26300.9711"))
            {
                Check(app.Execute(new(HostCommandKind.AssignPersonal)).Succeeded);
                Check(!app.Snapshot().Settings.SwitchingConsent && app.Snapshot().Settings.SwitchingConsentFamily == "");
            }
            using var upgrade = new Fixture();
            using (var app = SwitchRegressions.Active(upgrade, "26300.9711")) { }
            upgrade.Settings.BeforeReplace = () => throw new IOException("Synthetic revocation save failure.");
            try { using var failed = upgrade.App(false, "26400.9711"); Check(false); } catch (IOException) { }
            upgrade.Settings.BeforeReplace = null;
            using var session = upgrade.Session(); Check(session.Status == OfframpShift.Persistence.PersistenceStatus.Ready);
        });
    }
}
