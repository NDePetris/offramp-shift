# Owner smoke tests after the exact read-only build update

These tests are **NOT RUN** by the agent. They are short owner-evaluation smokes,
not accounting accuracy, uninterrupted monitoring or unattended daily-use acceptance.
Use ordinary unelevated Windows PowerShell outside Codex's constrained tool sandbox,
on Windows 11 25H2 Professional **26200.9711/x64**. Record exact revision, two-monitor
layout/resolution/scaling/DPI, privileges, results and cleanup. Save work first.
Stop on failure; retain it separately from any deliberately initiated retry.

## Production observation path — bounded, no accounting

From this compatibility branch, independently select Personal in Task View and
keep the terminal on it. This uses the production observation library through the
existing probe, rather than the compatibility diagnostic's direct reader entry.

```powershell
Set-Location G:\Projects\offramp-shift
git branch --show-current
git rev-parse HEAD
./spikes/WindowsObservation/run.ps1 build
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$raw = & ./spikes/WindowsObservation/run.ps1 watch 10 --desktop
$probeExit = $LASTEXITCODE
if ($probeExit -ne 0) { throw "Observation failed: exit $probeExit" }
$summary = $raw | ConvertFrom-Json
$summary | Select-Object build, architecture, monitors, desktop, desktopCount,
    hooks, sessionNotifications, powerNotifications, pump, foreground, input,
    accounting, certifiedMs, dropped, callbackErrors, samples, reason |
    ConvertTo-Json -Compress
if ($summary.desktop -ne 'Available' -or $summary.dropped -ne 0 -or
    $summary.callbackErrors -ne 0 -or $summary.certifiedMs -ne 0) {
    throw 'Observation smoke failed; retain the anonymous summary'
}
```

The existing probe has sensitive current-state fields, so the command captures
its final JSON only in memory and prints selected anonymous fields. **Do not save
or paste `$raw` or `$summary`**, which contain current GUID/app-key fields. No
per-event ledger or accounting store is created. Require build 26200.9711, desktop
Available, expected desktop count, healthy pump/hooks, zero drops/callback errors,
accounting Gated and zero certified time. Independently confirm Task View again
afterward. A successful process exit alone does not prove desktop health.
Optionally repeat the same ten-second command after manually selecting Work;
keep each attempt's outcome distinct. Do not issue automated switches.

After exit, verify no owned `ObservationProbe` helper remains. Do not kill other
dotnet processes. Use Task Manager locally and return only anonymous cleanup
status, never process lists, GUIDs, app keys, titles or browsing information.

## WPF app — isolated source checkout, draft PR #11 unchanged

This compatibility branch is based on main and **does not contain the WPF app**.
An existing PR #11 binary retains its old policy. To test the new policy with the
draft app without modifying its branch, the following creates a detached checkout
of its reviewed revision and overlays **only `Evidence.cs` from this compatibility
commit**. That file was unchanged between main and the pinned PR #11 revision.
The temporary overlay is an integration experiment, not an update to PR #11.

First exit any running Offramp Shift app through **tray Exit** and confirm it
finished cleanup, so the existing singleton cannot redirect this launch. The app
uses the current user's normal `%LOCALAPPDATA%\Offramp\Shift` data; it has no
supported CLI override. This WPF smoke deliberately writes normal settings and
accounting checkpoints. Preserve that folder while the app is stopped if desired.
Do not confuse these writes with the diagnostic's historical no-write result.
Previously enabled/unpaused monitoring may resume if all saved prerequisites
remain valid; otherwise consent, current-build role assignment and explicit Start
are required. No settings are silently rewritten by this compatibility patch.

```powershell
$repo = 'G:\Projects\offramp-shift'
$compat = git -C $repo rev-parse spike/windows-26200-9711-compatibility
if ($LASTEXITCODE -ne 0) { throw 'Compatibility commit unavailable' }
$smoke = Join-Path $env:TEMP ('OfframpShift-WpfSmoke-' + [guid]::NewGuid())
git -C $repo worktree add --detach $smoke 7026b55517c36cd73fc91fce093cc6c9011a7586
if ($LASTEXITCODE -ne 0) { throw 'Detached checkout failed' }
$policySource = git -C $repo show "${compat}:src/OfframpShift.WindowsObservation/Evidence.cs"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read approved policy source' }
$policySource | Set-Content -LiteralPath (Join-Path $smoke 'src/OfframpShift.WindowsObservation/Evidence.cs') -Encoding utf8
git -C $smoke diff --stat
# Expected: only src/OfframpShift.WindowsObservation/Evidence.cs.
# Use the already-pinned SDK from the original checkout for the detached checkout.
$env:DOTNET_ROOT = Join-Path $repo '.tools/dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
Push-Location $smoke
try { ./run.ps1 app } finally { Pop-Location }
```

Record `$compat` and the pinned draft revision; this combined working tree is not
a single tested commit. `run.ps1 app` performs locked restore and a Release build
before the deliberate launch. If you use a normal installed pinned SDK rather
than `.tools/dotnet`, omit the two environment assignments above.

Perform only the following short check:

1. Verify Home/Settings/tray load and the new build no longer causes Unsupported.
   Missing consent or stale/current-build bindings are still legitimate setup gates.
2. If needed, explicitly consent to experimental observation. Independently select
   Personal, assign it through the tray without activating the main window, then
   manually select Work and assign it likewise. Require distinct extant identities;
   never infer roles from desktop ordinals or copy raw IDs into a report.
3. Explicitly Start if monitoring is not already enabled. Observe approximately
   15 seconds on each manually selected desktop. Require fresh available status
   and correct current role, with no automatic switching or window movement.
   Any reported usage/break progress remains Estimated; do not assess accuracy
   from this short exercise or claim complete interval coverage.
4. Pause and verify monitoring remains paused, then choose tray Exit. Confirm the
   app and its owned desktop worker exit; report cleanup failures honestly.

Do not exercise Explorer restart, sleep/lock, forced termination, full accuracy
or resource matrices as part of this short smoke. Issues #8 and #9 remain unresolved.
Leave the detached test checkout for inspection; no command above updates, pushes,
merges or releases PR #11. Do not launch the ignored automated-test snapshot as a
substitute for the deliberate owner steps.

Return anonymous selected observation output, both exit/cleanup outcomes, WPF
role/setup/pause observations and any failure/omitted step. Record PASS / FAIL /
BLOCKED / NOT RUN separately in [the evidence record](windows-26200-9711.md).
