# Offramp Shift — repository instructions

These instructions apply to all code and documentation in this repository. Follow the more specific instructions in a subdirectory if present.

## Product intent
Offramp Shift is a Windows desktop application intended to support intentional transitions between personal and work contexts. It belongs to the Offramp family alongside the Android app Offramp Fade, but this repository owns its own architecture, code, tests, and releases.

- Use ordinary, immediately understandable primary labels for navigation and actions. Driving references can appear in secondary copy or subtle visuals, never as the only explanation of a control.
- Prefer privacy-preserving, local-first operation and minimum necessary permissions. Do not add analytics, telemetry, background network transmission, or remote services without an explicit product decision.
- Do not record full browsing URLs, page titles, tab histories, or content. Domain-level attribution is experimental and is not an approved production feature.
- Prioritize accessibility, reversibility, explicit user control, and safe failure behavior over an apparently seamless experience.

## Present status and hard safety gates
The current code is a feasibility probe, **not** the production application. Read `spikes/VirtualDesktop/README.md` and its validation documents before modifying it.

- **Automatic Windows virtual-desktop switching: NO-GO.** Existing GUID-based switching is experimental, explicitly invoked, and build-gated. Automated policy tests do not establish live reliability. Do not enable automatic switching, extend build allowlists, move windows, implement retries, or silently recover from an indeterminate switch without a newly documented live-validation decision.
- **Production browser-to-window website attribution: NO-GO.** PR 0B is a separate feasibility effort with simulated and transport-level evidence but no successful real Chrome/Edge attribution matrix. Do not wire website attribution into automatic intervention or treat synthetic correlation as live validation.
- **Distribution/licensing: undecided.** The existing GPLv3 license applies to development; do not select a public distribution model or change licensing without an explicit decision. See `docs/decisions/licensing.md`.

Keep these gates true even if tests pass. Do not infer feature approval from a merged spike.

## Working agreement
1. Inspect the relevant code, tests, and written decision gates before proposing a change.
2. For nontrivial or risky changes, state the intended behavior, affected components, failure modes, and validation plan before editing.
3. Prefer small, focused PRs. Do not mix harness changes with feasibility behavior or production features.
4. Preserve warnings-as-errors, explicit failure reporting, conservative defaults, and deterministic logic that can be tested without operating-system access.
5. Add or update tests when behavior changes. Keep real Windows/hardware claims separate from fakes, simulations, and CI checks.
6. Update relevant README, architecture or decision documentation when a feature boundary, interface, operational assumption, or go/no-go status changes.
7. Do not merge, publish, deploy, or alter active PRs unless explicitly requested.

## Validation
For the present Windows virtual-desktop probe, from a Windows PowerShell terminal with the pinned .NET SDK:

```powershell
./spikes/VirtualDesktop/run.ps1 test
```

This runs a build, assertion-based tests, and worker-isolation checks. It is the current safe CI baseline; it does **not** prove compatibility with Windows shell COM, actual virtual desktops, browser windows, multiple monitors, or reboot behavior.

Use `snapshot`, `safety-test`, `switch`, and live validation checklists only as deliberate, documented local exercises on supported Windows configurations; do not schedule actual desktop switching in CI.

In PR descriptions, distinguish **automated pass**, **manual pass with precise OS/build/hardware**, **untested**, and **blocked/NO-GO**. Never claim safety from test counts alone.
