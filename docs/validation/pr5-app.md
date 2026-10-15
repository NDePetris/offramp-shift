# PR 5 validation — owner evaluation only

Base: `e73fe57` (merged PR 4), branch `feat/first-usable-windows-app`.
This record distinguishes automated work from live Windows acceptance. No actual
monitoring session, desktop switch, Explorer restart, lock or suspend exercise
was performed during implementation.

## Automated results

Windows 11 25H2 **26200.9711/x64**, pinned .NET SDK 10.0.401, Release,
warnings as errors. This machine is now outside the unchanged 26200.9457/x64
observation allowlist. Native monitoring acceptance is blocked here; the build
and noninteractive results below do not expand that gate.

| Check | Result | Evidence boundary |
|---|---|---|
| Core | PASS: 67 tests / 38,679 assertions | Synthetic domain observations |
| Persistence | PASS: 65 tests / 618 assertions | Real local SQLite, injected faults; five owned Windows forced-termination scenarios reported separately |
| Application host | PASS: 25 tests / 103 assertions | Fake providers/clock, real temporary SQLite and settings; no native observation |
| WPF application | PASS: 9 tests / 70 assertions | Synthetic states/theme/high-contrast artwork selection, packaged canonical resources, actual WPF layout/automation properties, current-user activation process, actual WinExe non-COM worker fault modes |
| Windows observation | PASS: 41 tests / 10,237 assertions | Existing synthetic estimator and real local SQLite integration |
| Virtual desktop probe | PASS: 58 tests + three isolation checks | Fake providers and owned worker faults; no shell COM/switching |
| Browser probe | PASS: 2,053 assertions + five mock groups + transport checks | No Chrome/Edge interaction or attribution validation |

Reproduction: `./run.ps1 test`, `./spikes/WindowsObservation/run.ps1 test`,
`./spikes/VirtualDesktop/run.ps1 test`, `./spikes/BrowserAttribution/run.ps1 test`.
Root/observation format verification and final diff checks PASS. The UI skill's
mechanical detector returned no findings (not a native accessibility certification).
CI retains all prior jobs; the root Windows job now
also builds/tests the host and WPF app. No interactive COM tests run in CI.

Synthetic light/dark Home/Settings renders were inspected locally under ignored
`.tools/pr5-ui`. They exposed and verified correction of a read-only progress
binding and Fluent brush/style inheritance. These are render checks, not real
desktop, tray interaction, keyboard, Narrator or high-contrast acceptance.

## Manual acceptance — NOT RUN

For each exercise record exact app revision, Windows build/UBR and x64, runtime,
privileges, CPU/RAM, storage/filesystem, power mode and monitor layout/DPI. Use an
isolated local store for destructive/fault exercises; preserve ordinary data.
Perform only deliberate owner-led exercises on the allowlisted configuration.

- [ ] Launch with `./run.ps1 app`; consent, tray-based Personal/Work assignment,
  distinct-ID rejection and explicit first Start all work without console parsing.
- [ ] Restart retains settings/totals, resumes enabled unpaused monitoring only
  after fresh observation, and preserves Pause without downtime credit.
- [ ] Independently check Personal/Work/Other/Unknown, usage/break progress,
  completed reset, exhausted/grace/nudging and unavailable feedback. Unknown time
  never looks earned. No tray balloon/toast, switching or window movement occurs.
- [ ] Duration edits save before success; failed writes freeze accounting.
  Retry save retries the exact pending commit. Incompatible and clock-blocked data
  never offer reset. Deliberate recovery preserves originals before replacement.
- [ ] Reorder/rename desktops preserves GUID roles; deletion/recreation requires
  explicit reassignment. No index substitution. Unsupported builds show unavailable
  without undocumented COM activation.
- [ ] Explorer restart reports unavailable where recovery fails; explicit Retry
  monitoring honors cooldown and fresh baselines. Check owned helpers after retries.
  This is manual fallback validation, not closure of issue #8.
- [ ] Lock/unlock and suspend/resume retain totals but credit no uncertain gaps.
- [ ] Close-to-tray, tray reopen, second launch, pending-save Exit, normal Exit and
  Windows logout are reviewed; check process/handle cleanup. Actual crashes do not
  replay effects or downtime. Delayed native shutdown is reported honestly.
- [ ] Keyboard/tab/access keys, tray keyboard access, Narrator announcements,
  light/dark/system/high contrast and enlarged text work. Test narrow window sizes,
  100/150/200% scaling and mixed-DPI monitors without clipped controls.

## Canonical assets and remaining gates

The owner subsequently supplied `offramp-design-v0.1.zip`. Eight canonical source/
runtime files were copied byte-for-byte with archive/entry SHA-256 provenance in
`src/OfframpShift.App/Assets/Offramp/provenance.json`. Geometry and pixel values were
not altered, archive scripts were not executed, and proposed semantic mappings
were not promoted. The executable/window/tray use the family ICOs provisionally;
a differentiated Shift-specific icon remains pending. Light/dark header render
checks and synthetic high-contrast decorative-art exclusion pass. Actual shell
icon appearance, high contrast and DPI acceptance remain NOT RUN.

Both supplied ICOs retain all seven 16/24/32/48/64/128/256 frames, verified with
WPF decoding. Drawing.Icon decodes the tray sizes through 128px correctly here;
its chooser returns 128px for a 256px request. The tray requests 32px. No artwork
was changed to accommodate that decoder behavior.

Issue #8 (Explorer recovery) and #9 (independent accuracy/sustained reliability)
remain OPEN. No new two-hour measurement or mature transition matrix was run.
All prior automatic-switching/browser-attribution gates remain NO-GO. This PR does
not establish certified accounting, unattended daily use or production readiness.
No merge, deployment, release or license change is authorized or performed.
