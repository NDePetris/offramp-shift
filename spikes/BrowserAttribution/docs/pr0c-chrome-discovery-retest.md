# One Chrome discovery and attribution retest

One focused PR 0C Phase 1 retest, not the full matrix. Production website attribution and automatic desktop switching remain **NO-GO**. Website attribution is no longer an MVP prerequisite. If discovery remains unresolved or identity cannot be credibly established, stop and recommend deferring it; do not change policies, permissions, launcher type or binding checks to force a result.

Use the already isolated ordinary Chrome test profile and ordinary PowerShell 7. Keep personal browsing out of the foreground throughout capture. Record the running Chrome version/channel, Windows build, anonymous run label and tested commit. Store domains and raw observations only under ignored `artifacts/`. Do not save full URLs, titles, profile paths, browser debug logs or command lines.

## 1. Preserve the failed manifest and regenerate it

Stop any running monitor with Ctrl+C first. From `G:\Projects\offramp-shift`, use the actual IDs previously registered (they may be identical):

```powershell
$ErrorActionPreference = 'Stop'
$chromeProbeId = 'iohkegejabkcbefpdhlcomnflafpcecg'
$edgeProbeId = 'iohkegejabkcbefpdhlcomnflafpcecg'
$retestRoot = Join-Path (Get-Location) ('spikes/BrowserAttribution/artifacts/chrome-retest-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $retestRoot -ErrorAction Stop | Out-Null
$manifestFile = Join-Path (Get-Location) 'spikes/BrowserAttribution/artifacts/native-host.json'
Copy-Item -LiteralPath $manifestFile -Destination (Join-Path $retestRoot 'manifest-before-fix.json') -ErrorAction Stop
./spikes/BrowserAttribution/register-host.ps1 -ChromeId $chromeProbeId -EdgeId $edgeProbeId
$regenerated = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if ($regenerated.allowed_origins -isnot [array] -or
    $regenerated.allowed_origins -cnotcontains "chrome-extension://$chromeProbeId/") {
    throw 'Regenerated manifest lacks the required exact-origin JSON array; stop.'
}
'MANIFEST_ARRAY_PASS'
./spikes/BrowserAttribution/run.ps1 monitor |
    Tee-Object -FilePath (Join-Path $retestRoot 'observations.jsonl')
```

This preserves the malformed manifest locally, then uses the corrected registration script. Both registry views already point to this same file according to the owner; no new registration locations are needed. The `.cmd` runtime wrapper remains unchanged. Reload the extension in the isolated Chrome profile after the monitor starts.

## 2. Make one real Chrome native transport attempt

On `chrome://extensions`, choose the probe's **service worker** inspect link. Run the following once in that extension's worker console (not a website's console). It sends only a fresh protocol hello and prints fixed statuses. Do not print the port, messages, browser tab objects or error text.

```javascript
(() => {
  const session = crypto.randomUUID().replaceAll('-', '');
  const p = chrome.runtime.connectNative('com.offramp_shift.pr0b');
  let finished = false;
  const finish = status => {
    if (finished) return;
    finished = true; clearTimeout(timer); console.log(status);
    p.disconnect();
  };
  const timer = setTimeout(() => finish('CHROME_NATIVE_TIMEOUT'), 3000);
  p.onDisconnect.addListener(() => {
    const error = chrome.runtime.lastError?.message || '';
    finish(error.includes('Specified native messaging host not found') ?
      'CHROME_NATIVE_NOT_FOUND' : 'CHROME_NATIVE_DISCONNECTED');
  });
  p.onMessage.addListener(m => {
    if (m.v === 1 && m.type === 'challenge' && m.session === session &&
        /^[a-f0-9]{32}$/.test(m.nonce)) finish('CHROME_NATIVE_TRANSPORT_PASS');
  });
  try { p.postMessage({v: 1, type: 'hello', session}); }
  catch { finish('CHROME_NATIVE_DISCONNECTED'); }
})()
```

This temporary connection is a genuine browser-launched transport check, not domain/HWND attribution. It introduces no binding and disconnects after its first challenge or failure. The real extension worker has its own independent session. **If the status is not `CHROME_NATIVE_TRANSPORT_PASS`, stop: preserve the result and defer further discovery work for review.** No repeated attempts or launcher substitutions.

## 3. One popup binding and positive attribution

If transport passes, close worker DevTools. In the same isolated browser window, load a benign test page and wait for loading to finish. First corroborate its foreground identity: in a second ordinary PowerShell terminal at the repository root run the following, immediately focus that Chrome test window, and keep it foreground for at least five seconds:

```powershell
Start-Sleep -Seconds 3
./spikes/BrowserAttribution/run.ps1 snapshot
```

Return to the second terminal and record only the snapshot's HWND/PID/application/eligibility locally. It must identify the witnessed Chrome window and agree with the monitor interval; otherwise stop. This separate observation still uses the same native observer, so it is corroboration of the witnessed foreground, not independent attestation of Chrome's launch metadata. Return to the same test window and open the pinned probe action **once**. Record its exact fixed status.

If it reports **Bound**, close the popup and keep that test window foreground for five seconds. Then focus the terminal. Review the entire interval in the capture: application must be `chrome`, domain must match the independently witnessed active test hostname, and native HWND/PID must correspond to the test window. Review the focus-away interval too: the nonbrowser foreground must have null domain. Keep expected/observed hostname values local; report their anonymous test-host labels.

One correct output does not independently attest launch parent HWND, profile isolation or process ancestry. Report whether only the probe's checks accepted those assumptions or independent corroboration exists. Do not claim real positive attribution from `Bound` alone or an all-null capture.

If binding rejects, disconnects, times out, produces no correct positive observation, or emits any wrong domain, stop and record that result. Do not reopen the popup or weaken parent/foreground/ancestry checks. A failed binding remains unexplained unless evidence identifies the cause; it is not automatically proof of a fundamental identity failure.

## 4. Cleanup and return the outcome

Stop the monitor with Ctrl+C, close worker DevTools, unload the probe from the disposable profiles and close only their windows. In the original terminal:

```powershell
./spikes/BrowserAttribution/register-host.ps1 -ChromeId $chromeProbeId -EdgeId $edgeProbeId -Unregister
```

The unregister script removes its default-view keys only. A second registry-view key added during earlier owner troubleshooting may remain. Report that fact rather than deleting additional registrations blindly; with the test extension unloaded and monitor stopped it does not launch a host. Keep local evidence for review.

Return running versions, `MANIFEST_ARRAY_PASS`, native transport status, popup status (or NOT RUN), positive-domain result, focus-away result and any contradictory/wrong attribution. Keep domains, IDs and raw captures local. Stop for review; no Edge retest, full matrix, seamless-binding redesign or MVP implementation is part of this attempt.
