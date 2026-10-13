# PR 0C Phase 1 — owner-assisted real browser smoke test

Phase 1 only. Production website attribution and automatic desktop switching remain **NO-GO**. Do not start the 200-transition matrix or seamless-binding work until the Phase 1 evidence has been reviewed. Manual pairing here is a diagnostic technique, not a proposed production experience.

## Before starting

Use ordinary, non-administrator PowerShell 7 on the owner's Windows machine. Save unrelated work. This probe observes the current foreground, so keep personal browser windows out of the foreground while capturing. Do not sign in, enable sync, or permit private-mode access in the disposable profiles. Do not save browser debug logs, screenshots, full URLs, titles, command lines or profile paths as evidence.

Record the tested commit (`git rev-parse HEAD`), exact Windows build/UBR, browser versions/channels from their About pages, architecture, ordinary privilege level, monitor count/DPI and .NET/Node versions. Use anonymous labels such as Chrome-A and Edge-A. Installed file versions alone do not prove the version/channel of the running browser.

From the repository root, first run these commands if the independently recorded baseline is not available:

```powershell
./spikes/VirtualDesktop/run.ps1 test
./spikes/BrowserAttribution/run.ps1 test
```

These are automated checks, not live-browser evidence. Stop for investigation on failure. Do not run them alongside a live monitor: the synthetic transport test uses the same current-user singleton/pipe.

## 1. Open isolated browser instances

In PowerShell 7, from `G:\Projects\offramp-shift`:

```powershell
$ErrorActionPreference = 'Stop'
$smokeRun = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$smokeRoot = Join-Path (Get-Location) "spikes/BrowserAttribution/artifacts/phase1-$smokeRun"
New-Item -ItemType Directory -Path $smokeRoot -ErrorAction Stop | Out-Null
$chromeExe = @('C:\Program Files\Google\Chrome\Application\chrome.exe',
    'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe',
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe") |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$edgeExe = @('C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Microsoft\Edge\Application\msedge.exe') |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$chromeExe -or !$edgeExe) { throw 'Required browser not found; stop and report.' }
# These visible windows are the deliberate interactive test surfaces.
Start-Process -FilePath $chromeExe -ArgumentList @(
    "--user-data-dir=`"$(Join-Path $smokeRoot 'chrome-profile')`"", '--no-first-run', 'chrome://extensions')
Start-Process -FilePath $edgeExe -ArgumentList @(
    "--user-data-dir=`"$(Join-Path $smokeRoot 'edge-profile')`"", '--no-first-run', 'edge://extensions')
```

In each disposable browser: enable developer mode, choose **Load unpacked**, select `G:\Projects\offramp-shift\spikes\BrowserAttribution\extension`, and pin its action. Keep private-mode access disabled. Record each extension's 32-character ID locally. If loading is blocked by browser policy, stop and report the category; do not alter policy or substitute another browser binary.

## 2. Register and capture

In the same PowerShell session, replace the two placeholders with those IDs:

```powershell
$chromeProbeId = 'iohkegejabkcbefpdhlcomnflafpcecg'
$edgeProbeId = 'iohkegejabkcbefpdhlcomnflafpcecg'
./spikes/BrowserAttribution/register-host.ps1 -ChromeId $chromeProbeId -EdgeId $edgeProbeId
if ($LASTEXITCODE -ne 0) { throw 'Registration failed; stop.' }
./spikes/BrowserAttribution/run.ps1 monitor |
    Tee-Object -FilePath (Join-Path $smokeRoot 'observations.jsonl')
```

Registration changes only the two named HKCU host keys. A foreign registration must stop setup. The local SDK selects the existing `.cmd` launcher; do not replace it merely to get a passing result. Inspect the named host in both 32-bit and 64-bit registry views if launch fails; report presence/expected-manifest correspondence without copying paths. Do not change a shadowing registration.

The monitor holds this terminal until Ctrl+C. Reload both extensions **after** the monitor starts. Do not pipe arbitrary browser logs into this capture. Domains and per-action expected/observed hostnames belong only in ignored local artifacts.

## 3. First positive gate, separately in Chrome and Edge

Use Chrome-A first, then Edge-A. Open a stable benign HTTP(S) test page (for example the reserved example test hosts). Wait for loading to finish. Open the pinned probe action and record the exact fixed status text and the time of the attempt in a local ledger copied from `validation-template.csv`.

- If **Bound** appears: close the popup, leave the browser foreground for five seconds, then focus the terminal to inspect the captured interval. The expected output application is `chrome` or `msedge` (not the literal string `application:chrome`), the domain must match the independently witnessed active test hostname, and HWND/PID must remain associated with that window.
- Independently corroborate the foreground HWND/PID by keeping only that browser's test window open, checking its PID in Task Manager's Details view, and comparing focus-away/return intervals. Do not save Task Manager command lines. This verifies observable foreground correspondence only; a “Bound” response does **not** independently prove the launch parent HWND, extension window-ID mapping or ancestry. If these need direct corroboration, stop and request narrow instrumentation rather than declaring them established.
- If binding is rejected, times out or disconnects: preserve the attempt and report the fixed status. Do not repeatedly retry. If a later diagnostic establishes only a missing-worker startup race, one separately recorded reopen is permitted. No identity check may be loosened.
- If the popup says Bound but no correct domain appears, record a failed positive attempt and stop for focused diagnosis. An all-null capture is not a pass.

## 4. Small fallback/lifecycle check, only after the first positive

For each browser with a successful positive:

1. Focus an ordinary nonbrowser application for five seconds. Its interval must have null domain; a background browser's domain must never appear as that application's attribution.
2. Return to the bound test window for five seconds and require a second positive observation.
3. Open a second ordinary window in that disposable profile, visit a different benign hostname, and leave it **unbound**. Its foreground interval must have null domain. Return to the bound window and require recovery.
4. Disable then re-enable the extension on its extensions page. Return to the original test page **without opening the action**; domain must remain null until explicit rebinding. Then bind once and require a correct positive again. Record every failure and any transient wrong domain, not just settled output.

Use one local ledger row per witnessed action/attempt and keep all intervening capture lines. Record approximate operator timing uncertainty; no precise latency distribution is claimed from this smoke test. Review the full intervals, including transition edges. Stop immediately on any wrong-domain sample, background/unbound attribution, unsafe data capture or contradictory identity evidence.

## 5. Cleanup and report

Stop the monitor with Ctrl+C. In the original terminal:

```powershell
./spikes/BrowserAttribution/register-host.ps1 -ChromeId $chromeProbeId -EdgeId $edgeProbeId -Unregister
```

Unload the extension from both disposable profiles and close only those test windows. Retain their ignored run directory for review; do not delete unrelated profiles or terminate all browser processes. No automatic retry, reconnection, desktop operation or startup task is part of this procedure.

Report: running browser versions/channels, Windows build, anonymous run label, popup status per attempt, positive/fallback/lifecycle PASS/FAIL/NOT RUN, any incorrect sample, HWND/PID correspondence and remaining identity uncertainty. Keep actual expected/observed domains and raw captures local. A sanitized summary may refer to test-host labels instead. If execution cannot be completed, report the exact blocked step; simulations do not replace it.

Stop for owner review after Phase 1. Even successful manual pairing does not establish seamless binding, production safety, profile isolation, rapid-race safety or the full acceptance matrix.
