# Offramp Shift

Offramp Shift is an early-stage, local-first Windows application concept for helping users keep personal and work contexts separate. It is part of the Offramp family, alongside Offramp Fade for Android.

**Current state: an owner-evaluation WPF/tray app, a shared monitoring host, transactional local checkpoints and isolated feasibility probes.** PR #11 completed the accepted PR 5 milestone: owner acceptance Tests 1–7 were reported PASS; Tests 8–12 remain deferred / NOT RUN. Accounting is experimental and Estimated. This does not establish production readiness, certified accounting or unattended daily-use acceptance; Explorer recovery (#8) and sustained reliability (#9) remain unresolved. PR #13 completed the bounded exact-build read-only compatibility work. Automatic switching and production website attribution remain NO-GO.

## Development

- Windows x64 and the .NET SDK specified by [global.json](global.json).
- See [the virtual desktop probe README](spikes/VirtualDesktop/README.md) for setup, constraints, and manual test procedures.
- Run the current automated checks with PowerShell:

```powershell
./spikes/VirtualDesktop/run.ps1 test
./spikes/BrowserAttribution/run.ps1 test
./spikes/WindowsObservation/run.ps1 test
./spikes/WindowsObservation/compatibility.ps1 test
./run.ps1 test
./run.ps1 format
```

Launch the Windows x64 app for deliberate owner evaluation with `./run.ps1 app`.
First setup requires explicit experimental-observation consent, Personal/Work GUID
assignments and Start monitoring. Use the tray assignment menu on each manually
selected desktop without opening the main window first. Previously enabled
monitoring resumes on later app launches only with valid settings and fresh
providers; Pause persists. No Windows-login startup is configured. The native
desktop observer remains restricted to the two approved exact builds,
26200.9457/x64 and 26200.9711/x64. The separate switching gate is unchanged.

See [Windows app architecture](docs/architecture/windows-app.md) and
[PR 5 validation](docs/validation/pr5-app.md) and the
[prioritized owner acceptance checklist](docs/validation/pr5-owner-acceptance.md).
The [PR 6 setup and feedback checklist](docs/validation/pr6-setup-feedback.md)
tracks the focused setup, tray-error and first-close changes separately from the
deferred PR 5 acceptance exercises.

PR 9 adds explicitly selected **Interactive window** reminders, with immediate
exhaustion prompts, durable one/five-minute Personal-only extensions, and a
configurable repeat interval. Existing Windows notification users retain their
preference and timing. Focus is best effort and desktop switching remains absent.
See the [short PR 9 owner checklist and safety boundaries](docs/validation/pr9-interactive-nudges.md).

CI uses these automated test commands on Windows runners, with Node.js 24 for the browser probe. Browser tests use mocked extension APIs and synthetic native callers; neither job interacts with a live browser or switches desktops. CI cannot establish actual virtual-desktop or browser-to-window compatibility.

Core and persistence target platform-independent `net10.0`. Core tests use synthetic observations and a fake clock; persistence tests use real temporary SQLite databases and injected interruption faults. The root runner builds/tests Core, Persistence, ApplicationHost and the Windows app and verifies their formatting. Persistence introduces pinned Microsoft.Data.Sqlite with bundled native SQLite and locked transitive dependencies; initial restore/audit needs NuGet access, and subsequent locked restores can use cached packages. Core/Persistence contain no runtime network or native observation code; Windows integration is owned by the application host. See [core monitoring architecture](docs/architecture/core-monitoring.md), [local persistence architecture](docs/architecture/local-persistence.md) and [dependency notices](THIRD-PARTY-NOTICES.md).

## Architecture and decisions

- [Virtual-desktop feasibility results](spikes/VirtualDesktop/docs/pr0-results.md)
- [Windows application host and lifecycle](docs/architecture/windows-app.md)
- [Core monitoring domain architecture](docs/architecture/core-monitoring.md)
- [Transactional local persistence architecture](docs/architecture/local-persistence.md)
- [Best-effort monitoring architecture and checkpoint migration](docs/architecture/best-effort-monitoring.md)
- [PR 4 live accounting and resource validation](docs/validation/pr4-accounting.md)
- [PR 4 bounded investigation closeout and remaining limitations](docs/validation/pr4-closeout.md)
- [Local storage decision](docs/decisions/local-storage.md)
- [PR 2 validation and remaining durability checks](docs/validation/pr2-persistence.md)
- [PR 3 Windows observation investigation and certification checkpoint](spikes/WindowsObservation/README.md)
- [Manual Windows validation plan](spikes/VirtualDesktop/docs/manual-validation.md)
- [Browser-attribution feasibility probe](spikes/BrowserAttribution/README.md)
- [PR 0C Phase 1 owner-assisted smoke test](spikes/BrowserAttribution/docs/pr0c-phase1-smoke.md)
- [PR 0C Phase 1 evidence and pending gates](spikes/BrowserAttribution/docs/pr0c-phase1-results.md)
- [Development licensing and distribution decision](docs/decisions/licensing.md)
- [Agent and contributor engineering conventions](AGENTS.md)

The virtual-desktop feasibility spike is intentionally gated to the tested Windows build. Automatic desktop switching remains **NO-GO** until outstanding live validation gates are satisfied. Browser domain-to-window attribution is a separate experimental track and is likewise **NO-GO** for production use pending live proof.

Website-level attribution is not an MVP prerequisite. The core provides application-level classification, accounting, break resets, and advisory decisions. The app uses experimental read-only context observation; users change desktops themselves through Windows.

## Project conventions

Offramp uses clear conventional navigation and action labels, with occasional subtle driving-inspired supporting copy. The intended experience is private, understandable, reversible, and accessible. App-specific architecture, tests, and release decisions live in this repository rather than in shared cross-platform code.

No public distribution model has been selected; see the licensing decision above.
