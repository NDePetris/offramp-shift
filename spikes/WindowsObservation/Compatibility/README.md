# Windows 26200.9711 read-only compatibility investigation

Issue [#12](https://github.com/NDePetris/offramp-shift/issues/12), based on main
`e73fe5763ca510eab19dc3d04abcd4408ec19234`, branch
`spike/windows-26200-9711-compatibility`. PR #11's branch is untouched.
The owner reported PASS for all three bounded live exercises on the original
source digest `1251F5DD1D53BFFFA672597BDA820B48142C629EEAC540BFEC6F995D412C834D`
and explicitly approved adding 26200.9711/x64 for experimental owner-evaluation
monitoring. See [the evidence and approval](../../../docs/validation/windows-26200-9711.md).
The commands below remain available for deliberate owner use; no additional live
tests were performed automatically. The updated production/WPF path has a separate
[smoke procedure](../../../docs/validation/windows-26200-9711-smoke.md).

## Isolation and exact guards

The shared production predicate `DesktopObservationPolicy.IsSupported` in
`src/OfframpShift.WindowsObservation/Evidence.cs` now accepts exactly builds
`26200.9457` and `26200.9711`, with architecture `X64`. `DesktopWorker` checks it
in its constructor before spawning a child and in `Run` before constructing
`DesktopComReader`; both guard implementations and the ABI remain unchanged.
Ordinary snapshot/watch, desktop confirmation, monitor and integrated app launches
can now use read-only observation on the new build, subject to their existing
opt-in/setup gates. This does not permit switching or broader compatibility.

This standalone executable privately compiles the **existing** `DesktopWorker.cs`,
`Evidence.cs`, `Native.cs`, and portable confirmation gate via source links. There
are no copied COM declarations, new production APIs, friend assemblies, shared
policy exceptions, or references to this executable from production. The linked
production worker is used only to check rejection in `dry-run` when the current
build is unsupported (that check is skipped on approved builds);
live experiments instantiate the existing internal `DesktopComReader` directly,
in this executable's separately guarded STA child. Dormant foreground/native
helpers are not used to collect application identity or activity.

Only current GUID, GUID topology and shell generation are read through the
existing ABI. Its manager declaration ends before any switch slot. The parent
validates nonempty, unique topology (1..64 desktops), current membership and
stable shell generation/topology across reads. Desktop roles are ephemeral GUID
comparisons in memory; foreground window equality merely corroborates that the
same temporary confirmation window remained active during a manual read.

There is no switching, movement, pinning, foreground activation, synthetic input,
monitor/accounting host, persistence store, service, elevation or runtime network
access. Restore has cleared package sources and no external dependencies. Existing
interface provenance/license notice is [here](../THIRD-PARTY-NOTICES.md).

Live parent and child independently require Windows 25H2, **exact** build/UBR
26200.9711, x64 OS and process, interactive unelevated execution, and the explicit
experimental argument. The wrapper also requires operator attestation of running
outside a constrained tool sandbox and an anonymous monitor layout/DPI description.
An unsupported environment stops before COM. Do not broaden it to accommodate a
different update. Process isolation cannot guarantee Explorer survives a wrong ABI.

Each IPC read, including startup and pipe writes, has a two-second deadline and
a 4,096-character reply limit. First failure is terminal; there are no retries.
The parent kills only its owned child and waits up to one second for confirmed
exit. A child watchdog exits if the parent dies or after eleven minutes. The
manual exercise has a ten-minute limit, 21 confirmations, and no event queue;
repeat has 30 reads spaced approximately one second apart. No event subscription
means event-drop coverage is **not applicable**, not a measured zero-loss claim.

GUIDs and shell IDs travel only through private parent/child pipes and memory.
No raw IDs, process information, application keys, titles, URLs or event histories
are printed or saved. Output contains final health/counts, anonymous comparisons,
failure category, latency p50/p95/p99/max, and cleanup outcomes. Latency includes
IPC and first-worker startup. Failed read attempts are included in distributions;
timeouts are measured parent deadlines, not completed COM latency. Prompts show
only the expected A/B role and confirmation step.

## Prepare first — no experimental COM

Use ordinary **non-administrator Windows PowerShell**, outside Codex's tool
sandbox, from this checkout. Save work before any experimental read. Do not
start the normal app/monitor as part of these experiments.

```powershell
Set-Location G:\Projects\offramp-shift
git branch --show-current
./spikes/WindowsObservation/compatibility.ps1 test
./spikes/WindowsObservation/compatibility.ps1 dry-run
```

Require test PASS and exit 0. Dry-run prints edition/release, exact build/UBR,
OS/process architecture, elevation, interactive status, monitor count and runtime.
On 26200.9711/x64, require `eligibleForOptIn: true`, `productionSupported: true`,
`productionEntryGuardsRejectedWithoutWorker: false`, `comActivated: false`.
The false rejection field means the unsupported-build check was skipped, so this
dry-run still never launches a supported production worker or activates COM.
`test` additionally compiles the actual worker source in a separate executable
with test-only registry/runtime/native stubs and checks both entry guards in twelve
unsupported-build/architecture cases with zero child/native activation. Stubs are
never linked into live projects.
The wrapper records HEAD, dirty-tree state, compiled-source SHA256 and SDK each
time, and rebuilds before running so a stale diagnostic binary is not reused.
Retain that provenance with each summary. A dirty working tree must be identified
by its source digest; do not claim a commit alone identifies it.

Independently verify Windows Settings reports Windows 11 25H2, 26200.9711.
Record monitor count, relative layout, resolution and per-monitor scaling/DPI
from Display Settings, without device names or serial numbers. Set the following
variable to those **actual** anonymous facts before live commands:

```powershell
$layout = 'REPLACE with actual anonymous display layout, resolutions, and per-display scaling/DPI'
```

Stop if any required environment fact differs or is unavailable. SDK is pinned
by `global.json` to 10.0.401/latest patch. Outside-sandbox status and display details
are operator attestations, not automatically verified facts.

## Stage 1 — deliberate one-shot isolated read

In Task View independently select Personal; keep the console there. Then run:

```powershell
./spikes/WindowsObservation/compatibility.ps1 once -ConfirmExperimentalReadOnly -OutsideToolSandbox -MonitorLayoutAndDpi $layout
$LASTEXITCODE
```

This is the **first command that activates experimental COM**. Require final
`PASS`, exit 0, one successful read, no read errors/timeouts,
`ownedWorkerExitConfirmed: true`, and `productionSupported: true`.
The anonymous count/current membership check cannot establish human role correctness.
Stop on FAIL/BLOCKED; preserve the failure summary and check Task View yourself.

## Stage 2 — short repeated read

Stay on the independently identified Personal desktop for the bounded run:

```powershell
./spikes/WindowsObservation/compatibility.ps1 repeat -ConfirmExperimentalReadOnly -OutsideToolSandbox -MonitorLayoutAndDpi $layout
$LASTEXITCODE
```

Require PASS/exit 0, 30 successful reads, no read errors/timeouts or missed sampling
deadlines, stable topology/generation, and confirmed owned-child exit. Cross-check
the selected desktop in Task View before and after. Read success alone does not
independently identify its human role; Stage 3 makes that comparison. A missed
deadline is an annotated timing failure even if all returned readings were valid.
Ctrl+C cancels, reports failure and cleans up; closing/killing the parent relies
on its child watchdog instead of a normal final summary. Check cleanup manually.

## Stage 3 — Personal/Work, pinned temporary window

```powershell
./spikes/WindowsObservation/compatibility.ps1 manual -ConfirmExperimentalReadOnly -OutsideToolSandbox -MonitorLayoutAndDpi $layout
$LASTEXITCODE
```

1. A temporary compatibility test window appears. In Task View, manually enable
   **Show this window on all desktops** for this window only. The harness does
   not pin anything. Do not pin your real applications for this exercise.
2. Independently select Personal in Task View and confirm it matches the prompt
   `1/21 ... Personal (A)`. Click the confirmation button in the temporary window.
3. Manually select Work in Task View and confirm step 2 there. Continue alternating
   Personal/Work/Personal, independently checking Task View at **every endpoint**.
   Wait for the title's next step before each click. Never press repeated clicks
   in place of an independently corroborated transition.
4. Complete 21 confirmations / 20 transitions. First A/B reads must be distinct;
   each later endpoint must match the expected extant binding, with healthy
   reads and stable shell/topology. A wrong/repeated identity fails terminally.
5. Require PASS/exit 0, 21 confirmations, 20 transitions, zero missed confirmations,
   errors/timeouts, and both worker/window cleanup confirmations. Report independent
   corroboration and manual pinning separately; software cannot certify your labels.
   The temporary window closes, removing its temporary pin setting. Closing it
   early cancels and reports FAIL; it does not count as a completed run.

No foreground hook, application identity or full activity-history claim is made.
The same pinned foreground window may remain active across desktop changes.

## Optional shell invalidation — separate owner action only

NOT RUN by default. **Only if you deliberately choose this extra test after saving
all work**, start a fresh `repeat` command above and restart Explorer yourself
through Windows while it runs. The old reader should stop with an explicit
`ReaderInvalidated`, `ComFailure`, `ShellGenerationChanged`, `WorkerExit`, or timeout,
and confirm owned-worker cleanup. A healthy old-generation reading is a failure.
The diagnostic never restarts Explorer or automatically replaces its reader.

Preserve that summary as the invalidation evidence even though its run result is
FAIL. Once Explorer has settled, deliberately initiate a new `once` command as a
separate explicit retry. Record its result separately. A fresh success establishes
only that explicit retry worked; it does not fix or validate automatic recovery.
If restart occurs outside the observation window, mark the scenario inconclusive.

## Post-run and evidence to return

Run `dry-run` again; productionSupported should be true on the newly approved
build, with comActivated false. Close only the temporary test window if it remains after cancellation.
Check Task Manager for any remaining `DesktopCompatibility` process (or `dotnet`
whose command line names **DesktopCompatibility.dll**); no such parent/worker
should remain after exit. Do not terminate unrelated dotnet processes. A leftover
owned worker is FAIL; record it, then terminate only that diagnostic process.

The harness never opens the app's settings/store. Independently check your normal
settings/store location has no unexpected writes. PR #11's existing unmodified
binary/branch retains its old policy; only a separately integrated build receives
this approval. Use the separate smoke procedure for that build and keep its
deliberate settings/accounting writes distinct from diagnostic no-write evidence.
Confirm no automatic desktop changes occurred and the temporary pin disappeared.

Return the **provenance and final JSON summaries**, exit codes, actual anonymous
display details, independent Task View confirmations, cleanup/settings observations,
and any failed/omitted steps. Keep PASS / FAIL / BLOCKED / NOT RUN separate. Retain
failed attempts; never overwrite them with an invisible retry. Do not return raw
GUIDs, process lists, titles, keys, browsing data, dumps or per-event histories.
Paste aggregate output only; there is no automatic evidence-file writer.

Update [the evidence record](../../../docs/validation/windows-26200-9711.md) after
reviewing any returned results. The owner has now approved the narrowly scoped
two-build shared predicate update in this PR. Additional builds or broader feature
approval still require a new decision. Caller impact includes snapshot/watch,
user-test, monitor and PR #11's host; read-only compatibility is separate from
BestEffort accuracy and recovery acceptance.
Automatic switching and production website attribution remain NO-GO; issues #8
and #9 remain open. No merge, release, deployment or licensing decision is implied.
