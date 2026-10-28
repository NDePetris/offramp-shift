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

Use a build containing both PR #29 and PR #30. Any future owner-run launch must
follow a normal incumbent-app exit; do not kill it, reset accounting, repair stores
or change assignments to make the diagnostic run. Repository cleanup does not
launch or stop the app or open the owner store.

**Storage compatibility:** PR #30 now integrates main containing merged PR #29.
The combined code retains timed Pause and checkpoint v4; SQLite schema/user_version
remains 1. Strict v1/v2/v3 migration, expiry, recovery and fail-closed persistence
are unchanged by capture. Never run a v3-only executable against the owner's v4
store, downgrade it or reset it. An incompatible/blocked app cannot produce the
next ordinary reminder.

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
PR. The original capture-only validation below was automated; live validation was
**NOT RUN at that stage**. The later bounded owner report is recorded separately
below. No authorization exception, release or deployment is included.

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

## Combined-build owner report — October 28, 2026

The owner reported a successful one-shot authorization capture on **Windows
26300.9711/x64** from local combined build
**bd4f9fc29ee58036b77a3a3f798f290d66079e03** (PR #29 head 7349469 plus PR #30 head
8e42c93). It retained checkpoint v4 and showed valid Personal desktop membership.
This is bounded owner-reported live success, not an agent-executed acceptance run.
No new hardware, monitor/scaling or privilege matrix was supplied with this report.

One reminder remained behind Chrome after foreground activation was refused.
Subsequent reminders appeared in front of Chrome and Codex, including tests across
desktops. Visibility has improved after PR #28 but remains intermittent; issue
[#22](https://github.com/NDePetris/offramp-shift/issues/22) stays open.

The successful capture did **not** reproduce or explain the earlier
`CandidateWrongDesktop` failures. Track that separately in
[#31](https://github.com/NDePetris/offramp-shift/issues/31). Future investigation
requires an explicitly armed one-shot capture of an actual rejecting invocation.
No application-specific, pinned-window or Windows API cause is established and no
authorization change is justified.

Timed Pause's three-step Windows owner smoke remains **NOT RUN**, as do the
previously deferred PR 5 Tests 8–12. This report does not establish sustained
monitoring accuracy, recovery, accessibility or unattended daily-use readiness.

## Repository integration checks — October 28, 2026

Main containing merged PR #29 was merged into the existing PR #30 branch without
rewriting history. Git merged the four overlapping files cleanly; manual review
confirmed both behaviors are retained. Before documentation updates, the merged
tree exactly matched bd4f9fc's tree (cebd5c2f52f642158114943376c431f97dba83e2).
Only the three validation/architecture documents changed afterward.

All seven required commands passed with pinned SDK **10.0.401**, warnings as errors:

| Command | Integrated automated result |
|---|---|
| `./run.ps1 test` | PASS: Core 86; Persistence 108 / 970 assertions; Host 343 / 3,312; App 164 / 195; zero failures |
| `./run.ps1 format` | PASS: verify-no-changes |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS: 58 fake-policy cases and owned non-COM isolation |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS: synthetic policy/protocol/extension/transport |
| `./spikes/WindowsObservation/run.ps1 test` | PASS: 41 tests / 10,300 assertions |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS: unsupported-entry guards, capture regressions and 16 synthetic/owned-process groups; no COM |
| `./spikes/WindowsObservation/run.ps1 format` | PASS: verify-no-changes |

Application checks used environment-only `UseArtifactsOutput=true` and isolated
`artifacts/repository-cleanup-checks`; probe checks used their established outputs.
Logs use ignored `artifacts/cleanup-*.log`. Tests exercise fake providers/clocks,
disposable SQLite and owned synthetic WPF/non-COM processes. They do not perform
owner acceptance. The running owner app and its isolated combined-build outputs
were preserved; cleanup did not open or modify owner data. Checkpoint payload
version remains 4 and SQLite schema/user_version remains 1.
