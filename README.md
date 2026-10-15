# Offramp Shift

Offramp Shift is an early-stage, local-first Windows application concept for helping users keep personal and work contexts separate. It is part of the Offramp family, alongside Offramp Fade for Android.

**Current state: an owner-evaluation WPF/tray app, a shared monitoring host, transactional local checkpoints and isolated feasibility probes.** Accounting is experimental and Estimated. PR 4's partial live evidence does not establish production readiness or unattended daily-use acceptance; Explorer recovery and sustained reliability remain unresolved. Automatic switching and production website attribution remain NO-GO.

## Development

- Windows x64 and the .NET SDK specified by [global.json](global.json).
- See [the virtual desktop probe README](spikes/VirtualDesktop/README.md) for setup, constraints, and manual test procedures.
- Run the current automated checks with PowerShell:

```powershell
./spikes/VirtualDesktop/run.ps1 test
./spikes/BrowserAttribution/run.ps1 test
./run.ps1 test
./run.ps1 format
```

Launch the Windows x64 app for deliberate owner evaluation with `./run.ps1 app`.
First setup requires explicit experimental-observation consent, Personal/Work GUID
assignments and Start monitoring. Use the tray assignment menu on each manually
selected desktop without opening the main window first. Previously enabled
monitoring resumes on later app launches only with valid settings and fresh
providers; Pause persists. No Windows-login startup is configured. The native
desktop observer remains restricted to exact build 26200.9457/x64.

See [Windows app architecture](docs/architecture/windows-app.md) and
[PR 5 validation and pending manual acceptance](docs/validation/pr5-app.md).

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
