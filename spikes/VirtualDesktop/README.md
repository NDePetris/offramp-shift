# PR 0: Windows virtual desktop feasibility

This is an experimental console probe, not the production application. It has no tray UI, monitoring/accounting engine, browser integration, startup registration, installer, network listener, or automatic intervention.

## Build and test

Prerequisites: Windows x64 and .NET SDK 10.0.401 (or latest patch in that feature band). The current development checkout has an ignored official SDK under `.tools/dotnet`; a normal .NET installation also works. `global.json` pins the SDK. No NuGet package dependencies are required; restore sources are explicitly cleared.

From the repository root, in PowerShell:

```powershell
./spikes/VirtualDesktop/run.ps1 test
./spikes/VirtualDesktop/run.ps1 snapshot
./spikes/VirtualDesktop/run.ps1 safety-test
./spikes/VirtualDesktop/capture.ps1
```

`test` builds with warnings as errors, runs a package-free assertion executable with a nonzero failure exit code, then checks real worker timeout, abnormal exit, and malformed input. Policy tests use fake providers and do not establish Windows compatibility. `safety-test` makes deliberately invalid requests against the real provider and requires zero attempted switches. It needs two existing desktops.

The live native adapter is intentionally enabled only for **26200.9457 / x64**. Other exact builds/architectures reject before shell COM activation. Do not expand this allowlist merely because an upstream library advertises a build family: inspect its ABI and repeat the validation matrix first.

## Bind roles without moving windows

Select the intended Personal desktop yourself in Task View, then:

```powershell
./spikes/VirtualDesktop/run.ps1 bind Personal ./spikes/VirtualDesktop/local-bindings.json
```

Select Work yourself, then:

```powershell
./spikes/VirtualDesktop/run.ps1 bind Work ./spikes/VirtualDesktop/local-bindings.json
./spikes/VirtualDesktop/run.ps1 verify ./spikes/VirtualDesktop/local-bindings.json
```

Bindings persist GUIDs, provider, schema version, and exact Windows build. Names and ordinal positions are not keys. Deleted GUIDs are never substituted. Both roles must differ. Invalid, deleted, or version-mismatched existing bindings are rejected; use a deliberately chosen new binding file to start reconfiguration. `verify` certifies identity presence only, not production switching capability.

## Explicit experimental switch

From Personal, after reading the limitations below:

```powershell
./spikes/VirtualDesktop/run.ps1 switch Work ./spikes/VirtualDesktop/local-bindings.json --confirm-experimental
./spikes/VirtualDesktop/run.ps1 switch Personal ./spikes/VirtualDesktop/local-bindings.json --confirm-experimental
```

Each command issues at most one switch and requires the opposite bound source. No command creates, deletes, reorders, pins, minimizes, or moves windows. The adapter calls `SwitchDesktop`, never `SwitchDesktopAndMoveForegroundView`. The destination is a GUID-resolved COM object, never an ordinal.

For the existing uniquely named Personal/Work setup, this explicitly enabled convenience experiment captures ten snapshots and one round trip:

```powershell
./spikes/VirtualDesktop/capture.ps1 -SwitchRoundTrip
```

It stops on the first rejected/indeterminate result, including before a return leg. It does not retry or silently restore the source. Check Task View manually after any failure. Raw local evidence and GUID bindings are ignored under `artifacts/`; repeat captures currently replace those files, so archive each run outside that directory before collecting the larger matrix. Commit only a reviewed, sanitized result summary.

## Isolation and safety boundaries

- The parent starts a hidden, disposable child using redirected stdio. Undocumented COM stays in that child on one STA thread.
- One bounded request is accepted per worker. The parent has a three-second deadline; result verification has a two-second budget inside the worker. Neither path retries.
- The parent captures source identity; the worker re-observes before and after resolving the destination. Changed source, topology, foreground HWND, monitor count, session, shell process, noninteractive input desktop, unsupported build, stale request, or identity mismatch rejects. Request leases use sleep-inclusive monotonic uptime plus a conservative UTC uncertainty check.
- Public `IVirtualDesktopManager` foreground membership is diagnostic corroboration only. It cannot overwrite the independently queried active desktop, including for pinned windows.
- Source checks and the shell call are not atomic. A user action or deletion can race the final check. A correct postcondition is evidence about the observed result, not proof that no transient wrong desktop appeared.
- Terminating a stalled worker cannot undo a request the shell already accepted. Timeout or abnormal exit during switching is **indeterminate**, not a guaranteed no-op. Never issue a corrective or automatic repeat switch.
- Process isolation protects the parent from ordinary helper crashes; it cannot guarantee the shell is immune to an undocumented ABI failure.
- There is no production automatic-switch enablement. Its approved go/no-go gate remains unfulfilled until the manual matrix passes.

## Sources and licensing

Public baseline: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager

Undocumented interface facts: https://github.com/Ciantic/VirtualDesktopAccessor/tree/ceb648849dcc5760a7f4ed223308ffe3de0b51c6 (see `THIRD-PARTY-NOTICES.md`). Narrow C# declarations follow that revision; no upstream DLL is installed or loaded. Unused slots are placeholders, not callable features.

The root GPLv3 license is retained for development. Public distribution and final licensing remain open owner decisions: see `docs/decisions/licensing.md`.

See `docs/pr0-results.md` and `docs/manual-validation.md` for evidence, limitations, and pending gates.
