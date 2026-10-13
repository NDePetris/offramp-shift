# Offramp Shift

Offramp Shift is an early-stage, local-first Windows application concept for helping users keep personal and work contexts separate. It is part of the Offramp family, alongside Offramp Fade for Android.

**Current state: feasibility work only.** This repository presently contains an experimental Windows virtual-desktop probe, not a production tray app. There is no approved automatic switching or production website attribution.

## Development

- Windows x64 and the .NET SDK specified by [global.json](global.json).
- See [the virtual desktop probe README](spikes/VirtualDesktop/README.md) for setup, constraints, and manual test procedures.
- Run the current automated checks with PowerShell:

```powershell
./spikes/VirtualDesktop/run.ps1 test
```

CI uses the same automated test command on a Windows runner. CI cannot establish actual virtual-desktop or browser-to-window compatibility.

## Architecture and decisions

- [Virtual-desktop feasibility results](spikes/VirtualDesktop/docs/pr0-results.md)
- [Manual Windows validation plan](spikes/VirtualDesktop/docs/manual-validation.md)
- [Development licensing and distribution decision](docs/decisions/licensing.md)
- [Agent and contributor engineering conventions](AGENTS.md)

The virtual-desktop feasibility spike is intentionally gated to the tested Windows build. Automatic desktop switching remains **NO-GO** until outstanding live validation gates are satisfied. Browser domain-to-window attribution is a separate experimental track and is likewise **NO-GO** for production use pending live proof.

## Project conventions

Offramp uses clear conventional navigation and action labels, with occasional subtle driving-inspired supporting copy. The intended experience is private, understandable, reversible, and accessible. App-specific architecture, tests, and release decisions live in this repository rather than in shared cross-platform code.

No public distribution model has been selected; see the licensing decision above.
