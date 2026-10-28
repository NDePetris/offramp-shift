# One-shot interactive reminder authorization capture

This diagnostic captures the worker invocation used by a real initial interactive
reminder authorization. It does not implement an authorization exception.

The owner reports repeated `CandidateWrongDesktop` failures across unrelated apps,
with both Task View all-desktop options unchecked. A separate standalone sample on
26300.9711/x64 returned S_OK/true current membership and S_OK/Personal hosting
identity, with stable endpoints. These observations concern different invocations;
neither establishes the cause of the actual rejections. The running application's
revision was not verified in the standalone sample. Do not attribute the failure
to Chrome, a particular foreground app, pinning or a Windows API defect without
new evidence.

## Owner procedure

Use a build containing this PR. Exit the incumbent app normally before launching
another build; do not kill it, reset accounting, repair stores or change assignments
to make the diagnostic run. Live validation is **NOT RUN** until the owner supplies
the captured result.

**Storage compatibility:** this PR targets main and excludes draft PR #29. Main
uses checkpoint v3; PR #29's timed-Pause branch uses v4. If the current app has
written v4, a main-only build cannot read it. Use a separate local integration of
this capture commit onto that existing app revision; do not downgrade or reset the
store. Such an owner-evaluation integration does not belong in either PR. An
incompatible/blocked app cannot produce the next ordinary reminder.

From a compatible checkout, build into isolated outputs in PowerShell:

```powershell
Set-Location G:\Projects\offramp-shift
$captureDotnet = (Resolve-Path .tools/dotnet/dotnet.exe).Path
$env:DOTNET_ROOT = Split-Path $captureDotnet -Parent
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$captureArtifacts = Join-Path (Get-Location) 'artifacts/reminder-capture-owner'
$captureProject = 'src/OfframpShift.App/OfframpShift.App.csproj'
& $captureDotnet restore $captureProject --configfile src/NuGet.Config --locked-mode '-p:UseArtifactsOutput=true' "-p:ArtifactsPath=$captureArtifacts"
if ($LASTEXITCODE -ne 0) { throw 'Restore failed; do not launch.' }
& $captureDotnet build $captureProject -c Release --no-restore '-p:UseArtifactsOutput=true' "-p:ArtifactsPath=$captureArtifacts"
if ($LASTEXITCODE -ne 0) { throw 'Build failed; do not launch.' }
& $captureDotnet (Join-Path $captureArtifacts 'bin/OfframpShift.App/release/OfframpShift.App.dll') --open-diagnostics
```

The launch option opens Diagnostics temporarily without changing its saved
visibility preference. It does not arm capture, enable monitoring/reminders or
bypass the singleton. An existing instance causes an explicit rejection; the flag
is not silently sent to a different executable.

1. Click **Capture next reminder check** once. The read-only **Reminder
   authorization capture result** changes from Idle to Armed.
2. Return to an ordinary affected app on the already-selected Personal desktop.
   Leave settings and assignments unchanged and wait for one normal interactive
   reminder attempt. Do not force a repeat or change pinning for this exercise.
3. Return to the still-open Diagnostics window. When state is Complete, select the
   text in **Reminder authorization capture result**, press Ctrl+A/Ctrl+C, and
   supply that JSON only. The pre-existing technical-identifiers section is a
   separate view; do not copy it as part of this report.

Closing Diagnostics does not cancel an armed request or erase a report. Keep the
temporary window open for retrieval when the saved visibility preference is off.
Shutdown clears both request and report; a restarted process begins Idle.
Completion consumes the request on success, early rejection or worker failure.
Another capture requires another explicit click. Armed means no initial attempt
has arrived yet; there is no diagnostic timer, polling, forced reminder or retry.

## Interpretation

The outer Stage and Authorized fields describe initial authorization. Host/event
checks may reject before the worker runs: Worker is then null, rather than
fabricated evidence. Worker Allowed followed by EventAfterDesktopCheck with
Authorized=false means the later original event check rejected. Allowed does not
prove a reminder was shown, focused or confirmed.

Worker reports include:

- Explicit Role, original Result and rejection Stage. This action targets
  OriginalForeground. Reminder HWND completion checks remain separate and do not
  consume the request. Role is not inferred from HWND equality.
- CurrentBefore/After and hosting/foreground identity categories: Personal, Work,
  OtherEnumerated, NotEnumerated, Empty or Unavailable.
- HRESULT numeric codes and fixed categories. OnCurrent is null when its query
  failed or was not reached; a failed identity query never yields a trusted ID.
- ValidWindow, VisibleWindow and TopLevelWindow metadata, never authorization
  inputs. Null means metadata was not obtained. GA_ROOT is compared to the exact
  checked HWND; no replacement HWND is queried.
- Available foreground/session checks and Personal/shell/topology comparisons.
  NotEvaluated means execution never reached the check, not Passed. A queried
  unknown session is Unavailable. Matching endpoints do not prove continuity or
  exclude a transient leave-and-return.

CandidateWrongDesktop normally leaves later stability checks NotEvaluated because
the original function returns there. Capture does not run them afterward. It does
not repeat membership queries. A successful initial check already queries hosting
identity twice because candidate and foreground are the same HWND; capture retains
both original calls and can show disagreement between them. Topology equality is
diagnostic only: it adds no rejection where the original function had none.

## Provenance and boundaries

Exact Windows build/UBR and OS/process architectures are reported. The diagnostic
revision comes from the **loaded WindowsObservation assembly's** informational
version. The running application revision comes separately from the loaded
OfframpShift.App entry assembly. Missing/nonconforming metadata or another entry
assembly gives Unknown. Neither value comes from a checkout/path lookup. App and
observation projects explicitly retain SDK source revision embedding. Build
committed sources: a revision alone cannot identify uncommitted edits or certify
that all loaded component assemblies match.

Arming is an in-memory latch outside the host command queue. It does not invalidate
the mailbox, revoke presentation/switch grants, issue accounting commands, wake the
owner loop or save preferences. Only BeginNudgePresentation consumes it. Stop wins
over an in-flight completion. The normal app continues its existing accounting;
capture creates no accounting session or storage operation.

Only the captured call uses the additive private capture-nudge operation through
the same isolated worker, build/architecture gates, two-second reply deadline,
4,096-character limit and owned-process cleanup. No deadlines are extended. Two
additional read-only HWND metadata queries do not affect authorization. There is
no file I/O or UI/JSON formatting in the monitoring owner's completion path. The
child emits one bounded categorical reply; the UI formats a changed snapshot using
the existing refresh mechanism. All original query order, short circuits,
authorization rules, transaction expiry and foreground protections remain intact.

The report contains no GUIDs, HWNDs, PIDs, process/app identities, executable paths,
titles, URLs, browsing data, exception messages or activity history. There is one
process-local result, no diagnostic file log, telemetry or network transmission.
No assignments, settings, Core, checkpoint shape or virtual desktops are changed.
Automatic switching, window movement, foreground forcing/retries and production
browser attribution remain NO-GO. PR 5 Tests 8–12 remain deferred/NOT RUN.

## Validation

Deterministic coverage includes Personal success; Empty/Work/OtherEnumerated/
NotEnumerated IDs; failed/unavailable queries; early NotEvaluated checks; explicit
roles; identical captured/uncaptured results and query sequences; metadata failure;
one-shot/concurrent consumption; stop/restart; host/event pre-worker rejection;
unsupported builds/architectures; owned unavailable non-COM worker; privacy; UI
activation without a host command; and temporary opening without saved preferences.
Fakes, disposable storage and owned WPF/non-COM processes do not prove live shell
membership or accounting accuracy. All seven required results are recorded in the
PR. **Live validation: NOT RUN.** No authorization exception, merge, release or
deployment is included.

All seven required checks passed with SDK **10.0.401**, warnings as errors:

| Command | Automated result |
|---|---|
| `./run.ps1 test` | PASS: 82 Core, 84 Persistence, 321 host, 155 app groups |
| `./run.ps1 format` | PASS |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS: fake policy and owned non-COM checks |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS: policy/protocol/extension/transport |
| `./spikes/WindowsObservation/run.ps1 test` | PASS: 41 groups, 10,300 assertions |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS: new capture regressions, existing gates and owned non-COM workers |
| `./spikes/WindowsObservation/run.ps1 format` | PASS |

Application checks used environment-only isolated outputs under
`artifacts/authorization-capture-checks` to avoid running-owner file locks.
Probe checks were subsequently run in their standard output directories because
some existing runners execute fixed DLL paths. The final compatibility log
explicitly confirms execution of the new captured/uncaptured sequence regressions
and the unavailable owned capture-worker check. Existing owner instances were
not stopped or changed. `git diff --check` also passed.
