# PR 0B: browser attribution feasibility probe

Experimental Windows-only probe. **Browser attribution is NO-GO pending live validation. PR 0 automatic desktop switching remains NO-GO.** Nothing in this directory switches desktops, records timers, connects SQLite, or implements a production extension/dashboard/rules UI.

PR 0C starts with the [Phase 1 real-browser smoke test](docs/pr0c-phase1-smoke.md) and [separate Phase 1 evidence](docs/pr0c-phase1-results.md). Stop for review after Phase 1; do not run the full 200-transition matrix until the binding approach and Phase 1 evidence have been reviewed. Manual popup pairing is diagnostic, not the intended production experience. Historical PR 0B references to “PR 3 website integration” mean the future production website-integration phase, not GitHub PR #3 (the engineering harness). Historical evidence is retained unchanged.

For the owner-reported Chrome discovery failure, see the [focused findings](docs/pr0c-chrome-discovery-findings.md) and use only the [one focused Chrome retest](docs/pr0c-chrome-discovery-retest.md). Registration now retains `allowed_origins` as a JSON array even when both extension IDs are identical. Website attribution is no longer an MVP prerequisite; application-level context detection with manual-first desktop control is under consideration, not implemented here.

## Build and automated tests

Windows x64, PowerShell 7, .NET SDK 10.0.401 (root `global.json`), Node.js 24. No NuGet/npm packages or downloads needed. The ignored `.tools/dotnet` SDK from PR 0 is supported.

```powershell
./spikes/BrowserAttribution/run.ps1 test
./spikes/BrowserAttribution/run.ps1 snapshot
```

`test` builds with warnings as errors, checks generated native manifest JSON without registration, runs pure policy/framing assertions, mocked extension tests and JavaScript syntax checks, then runs real child-process/Native Messaging stdio/current-user named-pipe transport tests with a **synthetic caller**. It does not drive Chrome or Edge. The transport test temporarily starts and stops only its own processes. Run in ordinary Windows PowerShell outside the Codex filesystem/process sandbox: process inspection and the pipe bridge were restricted inside that sandbox. Do not run as administrator.

## Install locally for manual validation

1. Start with disposable ordinary Chrome and Edge test profiles. Open `chrome://extensions` / `edge://extensions`, enable developer mode, and load unpacked `spikes/BrowserAttribution/extension`. Record the extension ID in each browser. Do not enable private browsing; manifest explicitly disallows it.
2. Register the narrowly named host for the current user, using actual IDs:

```powershell
./spikes/BrowserAttribution/register-host.ps1 -ChromeId '<Chrome extension ID>' -EdgeId '<Edge extension ID>'
New-Item -ItemType Directory -Force ./spikes/BrowserAttribution/artifacts | Out-Null
./spikes/BrowserAttribution/run.ps1 monitor | Tee-Object -FilePath ./spikes/BrowserAttribution/artifacts/chrome-run-01.jsonl
```

3. Reload both extensions **after** starting the monitor. Pin the action. In each ordinary browser window, open the probe action popup. Require “Bound”, then close it. A rejected binding is a test failure/fallback, never a reason to loosen identity checks. Retry once if the worker hello had not arrived; record the rejection.
4. Use the browser normally with the monitor left running. Each JSON line has UTC/monotonic time, foreground epoch, HWND/PID, executable name, nullable domain and classification reason. `domain:null` is application-only. Output is observation evidence, not an accounting stream; production would need its own consumer health/expiration handling.
5. Restart/disconnect invalidates pairing. Open the popup to reconnect, then rebind every window. Reloading the extension also requires rebinding. This manual bootstrap is a deliberate feasibility limitation.

Registration only changes two exact HKCU host keys and creates ignored local artifacts. It refuses foreign registrations. If the local SDK is used, an ignored `.cmd` wrapper sets its runtime path; no global PATH/runtime installation is needed. A standard machine-wide .NET 10 runtime instead uses the apphost directly. The registration wrapper/browser launch path itself remains untested with actual browsers.

Cleanup (use the same IDs):

```powershell
./spikes/BrowserAttribution/register-host.ps1 -ChromeId '<Chrome extension ID>' -EdgeId '<Edge extension ID>' -Unregister
```

Stop the monitor with Ctrl+C and unload the probe from all test profiles. Keep local evidence only as long as needed. Nothing registers startup/background tasks. Artifacts can reveal browsing domains; do not commit raw captures.

See [manual validation](docs/manual-validation.md), [architecture/security](docs/architecture.md), and [measured results and decision](docs/pr0b-results.md).
