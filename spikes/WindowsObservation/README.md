# Windows observation and interval-certification investigation (PR 3)

**Feasibility checkpoint: current-state observation is operational on the measured host;
strict continuous interval certification remains NO-GO. Live accounting is gated.**
Automatic desktop switching and production browser attribution remain NO-GO.
This directory neither references nor changes either existing feasibility spike.

PR 4 now adds an explicitly invoked best-effort `monitor` command after the owner
accepted Stage 1. It opens a durable session only for that command, uses Estimated
rather than Confirmed intervals. The bounded investigation is now closed with
partial functional evidence; full acceptance remains unproven. See
[the closeout](../../docs/validation/pr4-closeout.md) for failures and NOT RUN checks.
See [the architecture](../../docs/architecture/best-effort-monitoring.md) and
[live procedure](../../docs/validation/pr4-accounting.md). Original snapshot/watch
commands still emit uncertain diagnostic evidence and open no accounting session.
The read-only native mechanisms now live in `src/OfframpShift.WindowsObservation`.
PR 5 moves the sampler and monitoring loop to `src/OfframpShift.ApplicationHost`,
shared by this headless command and the WPF owner-evaluation app. The probe remains
available; final monitor aggregates are now nested under `diagnostics`, with native
latency/resources under `diagnostics.providers`. See the
[Windows app architecture](../../docs/architecture/windows-app.md).
The monitor host reuses the existing pinned SQLite dependency; its runner uses
root NuGet source mapping and locked restore. New host/test lock files pin transitives.

The original PR 3 approved scope stops before durable-session integration, a production host,
notifications, or the full acceptance matrix when useful certified intervals cannot
be justified. See [the evidence and decision](docs/feasibility-results.md).

## Build and run

Windows x64, ordinary user, pinned .NET SDK from the root `global.json`.
The executable probe and assertion tests share extracted observation/estimation
libraries. The monitor command references Persistence and its existing pinned
SQLite dependency; the diagnostic commands do not open storage. Locked build
restore uses the root NuGet source mapping and may download these existing packages.
No service, installation, registry writes, startup task, browser integration,
runtime networking is introduced.

```powershell
./spikes/WindowsObservation/run.ps1 test
./spikes/WindowsObservation/run.ps1 format
./spikes/WindowsObservation/run.ps1 snapshot
./spikes/WindowsObservation/run.ps1 snapshot --desktop
./spikes/WindowsObservation/run.ps1 watch 15 --desktop
./spikes/WindowsObservation/run.ps1 delivery-test
./spikes/WindowsObservation/run.ps1 isolation-test
```

`test` uses deterministic observation/Core evidence plus a real local SQLite
integration check; no native observation API is invoked by the assertion executable.
`format` verifies the probe, tests and extracted libraries without edits.
`snapshot` and `watch` deliberately exercise real native APIs. Run outside the Codex
filesystem/process sandbox when its build/process restrictions prevent operation.
`watch` prints only its final current state and aggregate counters, not a sample
history. Ctrl+C stops it. It runs for at most 600 seconds. It writes no files.

`--desktop` explicitly enables the experimental read-only COM helper, only on
**26200.9457/x64** or **26200.9711/x64**, the two exact owner-approved read-only
observation builds. All other builds reject before undocumented COM activation.
The newer build is approved for experimental owner-evaluation monitoring after
the bounded owner-reported compatibility checks; this does not certify accounting
accuracy, uninterrupted observation or unattended daily use.

For the separately isolated, exact **26200.9711/x64** update investigation in issue
#12, see [the compatibility harness and operator procedure](Compatibility/README.md).
Its live commands require deliberate operator initiation. The shared read-only
policy now includes the approved build; the separate switching allowlist remains
unchanged and automatic switching/browser attribution remain NO-GO. Evidence and
approval are recorded in [the compatibility record](../../docs/validation/windows-26200-9711.md).
See [owner smoke commands](../../docs/validation/windows-26200-9711-smoke.md) for
the updated production observation path and separate WPF integration check.

For a diagnostic Personal/Work mapping, supply distinct GUIDs deliberately obtained
on the corresponding desktops by the operator, without printing or using their names:

```powershell
./spikes/WindowsObservation/run.ps1 watch 15 --desktop --personal '<GUID>' --work '<GUID>'
```

Both supplied identities must still exist in the observed topology. They are ephemeral
command arguments, not a persisted binding format. Mapping is endpoint diagnostics only;
it never becomes the interval's scope. Renaming/reordering does not replace a GUID.
Deleted identities and a newly created desktop with the same name do not repair a mapping.

`foreground-test` briefly creates two visible probe-owned test windows, requests A/B/A
focus, and deliberately pauses only the probe's message loop for 2.5 seconds. It restores
the original foreground on a best-effort basis and removes its windows. Windows may
reject focus requests; that is reported as a failed/unavailable stimulus, not a hook pass.
It never sends input or switches desktops. Do not bypass Windows foreground restrictions.

`delivery-test` needs no focus request: it destroys a hidden probe-owned window while
the probe's pump is deliberately paused, then measures observed native event delivery age.
It validates a real delayed callback, not a natural OS worst-case latency bound.
`isolation-test` terminates only its own stalled/malformed-output workers, without COM
activation in those fault workers. There is no helper retry loop.

## Components and evidence boundary

PR 4 Stage 1 adds `user-test foreground` and `user-test desktop` for explicit
owner-assisted endpoint checks without forced foreground activation. See the
[procedure and evidence boundary](../../docs/validation/pr4-stage1.md). These
commands create temporary visible test windows and print aggregate results only;
they do not open an accounting or durable session. Desktop testing retains the
exact build gate and requires the operator to switch and pin the test window.

- `NativeLoop`: dedicated STA thread, hidden top-level window, foreground/destroy/input-desktop
  WinEvent hooks, this-session WTS notifications, suspend/resume subscription and timer.
  Callbacks enqueue immutable signals; they never resolve identity or mutate Core.
- `Sampler` / `Native`: current foreground process identity, last-input timestamp and
  current-session connection/lock flags. Limited-query handles are disposed. WTS reads
  only scalar state fields from the x64 layout, never user/domain strings. A sample batch
  is not atomic across capabilities.
- `DesktopWorker`: same executable in a separate STA process, one `read` request protocol.
  Manager vtable declarations end before switch slots. The helper reads current GUID and
  topology only, checks shell generation and brackets topology enumeration with current
  GUID reads. Those checks detect some races, not all change-and-return. No desktop names,
  window membership inference, binding writes or mutation methods are exposed.
- `EvidenceInvestigation`: portable deterministic logic. Emits positive, nonoverlapping
  **uncertain** `MonitoringObservation` intervals, keeping endpoint evidence separately.
  It reports sequence loss/reordering, receipt rollback, reconciliation gaps, endpoint/shell
  changes and possible sleep discontinuities. There is no certification enable flag.

One bounded 512-item queue connects the native loop and diagnostic consumer. Overflow
and callback failures are explicit counters. Per-hook generation time, monotonic receipt,
sequence and uptime remain transient. The consumer samples about once per second, after
the previous sample; a gap exceeding two seconds is explicitly reported. The helper has
a two-second response deadline, reports failure, and is disposed rather than retried.
Its resources are independently owned; terminating it cannot change desktops.

`ObservationClock` uses `Stopwatch` and a process-local epoch. Suspend/resume signals start
fresh epochs. Awake uptime can reveal some missing power transitions, but is only a
diagnostic cross-check. No interval crosses epochs. Same-epoch rollback preserves the
high-water boundary. Every reported elapsed gap remains uncertain; none fills accounting.

**No coordinator or durable session is opened by the original diagnostic live commands.** Before PR 4, the only Core accounting
exercise occurs in synthetic tests: uncertain adapter output earns no usage/break credit,
and interrupts an artificially established partial break without resetting confirmed usage.
No live sample can become `EvidenceConfidence.Confirmed`, even with all capabilities healthy.
There is no holdback/finalization scheme because no sound coverage watermark was found.

## Identity, privacy and limitations

Identity keys are SHA-256 of a process AUMID where provided, otherwise an uppercase-invariant
full executable image path, prefixed `aumid:v1:` or `exe:v1:`. HWND/PID are rechecked around
resolution; identities are not cached across reuse. Paths stay transient and are never
printed. These keys identify the queried process, are not a security identity, can change
when executables move, and do not resolve hosted apps behind a broker. They are not wired
into application classification. Elevated/protected/inaccessible processes may be unresolved.

Last-input age is session-specific and sleep-inclusive, can include synthetic input and
does not prove uninterrupted activity or inactivity. Reject future/half-wrap ambiguous
ticks and regressing reconstructed timestamps. No idle qualification or break credit is
performed here: Core owns qualification once a defensible interval policy exists.

Output is current health/state, an opaque process key, current desktop GUID/count, and
aggregate counters only. No title, URL, page data, input content, raw HWND/PID, executable
path, desktop name or raw event history is printed or persisted. Current GUID/key can still
be identifying; do not commit raw output or redirect extended sessions into an activity ledger.
There are no capture files or SQLite writes.

Hooks, WTS/power registrations, timer, windows/classes, process handles and COM objects
are explicitly released. The message loop cannot safely abort an in-flight documented
native call; shutdown/startup timeout is explicit, not a guarantee of OS-call cancellation.
Long-running leak/CPU/wakeup behavior and full native fault coverage remain unvalidated.

## Mechanism references

- [WinEvent hooks and sequential asynchronous delivery](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)
- [WinEvent generation timestamp](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nc-winuser-wineventproc)
- [GetLastInputInfo session scope and nonincremental timestamp](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo)
- [WTS notification lifecycle](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification)
- [WTS current lock state](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ns-wtsapi32-wtsinfoex_level1_w)
- [Suspend/resume registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registersuspendresumenotification)
- [QPC / Stopwatch and sleep-inclusive timing](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps)
- [Public virtual-desktop API: window membership, no current-desktop GUID query](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager)
- [Pinned experimental ABI reference](https://github.com/Ciantic/VirtualDesktopAccessor/tree/ceb648849dcc5760a7f4ed223308ffe3de0b51c6)

The documented desktop-switch WinEvent is an invalidation hint here; no assumption is made
that it is a complete virtual-desktop GUID transition stream. Registry polling, foreground
membership, equal snapshots, a responsive timer and a healthy hook are not coverage proof.
See [third-party notice](THIRD-PARTY-NOTICES.md); GPLv3 development licensing is unchanged.
