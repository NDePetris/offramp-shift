# PR 10 implementation boundary

Base verified October 18, 2026: PR #18 merged into main at
`63c3f075148a69fd36e95553b86f5985e7859cfa`; PR 9 implementation is present.

## Intended file and interface changes (before implementation)

- Extract `Probe.Core/Model.cs` and `Probe/NativeSession.cs` into
  `src/OfframpShift.Switching`. Keep the DesktopFeasibility protocol/policy and
  DesktopProbe native namespace for spike source compatibility. Probe.Core becomes
  a reference facade. There remains one switching COM declaration set.
- Add `ISwitchExecutor`, typed `SwitchOutcome`, single-use `SwitchAuthorization`,
  and a bounded asynchronous stdio supervisor to that library. A new worker entry
  in App/Program runs before WPF, singleton, settings or accounting initialization.
- Add a quick host authorization command and controller orchestration. A fresh
  read-only runtime capture checks the authorized nudge HWND, active Personal,
  health and strict membership. Capture does not reset estimation or call Core.
  The independently supervised native operation runs off both owner and Dispatcher.
- WindowsSettings v4 adds separate exact-build consent, default off. Strict v1–v3
  migration preserves reminder/appearance/assignment preferences; SQLite is untouched.
- NudgeWindow/Presenter add a primary explicit action, shared submission exclusion,
  progress and typed feedback, retaining extension/dismissal behavior. MainWindow
  Settings exposes consent and current capability restrictions.
- New host/WPF/policy regressions and a dedicated owner qualification checklist
  cover failure boundaries and retain all seven established checks.

## Safety and validation plan

No automatic switching, ordinal fallback, retries, rollback, input injection or
window movement. Exact-build runtime checks run before native activation. Normal
application eligibility retains 26200.9457/x64 only, with separate explicit
consent. 26200.9711/x64 and 26300.9711/x64 are diagnostic-only until a subsequent owner-reviewed
application capability decision; observation approval never enables switching.

One request has a two-second authorization lease and one bounded child lifetime.
The worker independently observes configuration/session/shell/foreground/topology,
resolves Work with FindDesktop, verifies identity, checks source again and calls
SwitchDesktop at most once. Post-call failure, crash, timeout or invalid protocol
can leave a delayed shell side effect; no cancellation promises a no-op.
Indeterminate completion disables this nudge's switching; Win+Tab is the fallback.
Feedback must survive ordinary monitoring closure of the window.

Native calls remain STA. Verification delays pump only the worker's Windows
message queue; synchronous COM calls may still hang, so parent process supervision
is the main bound. A separate three-second worker watchdog exits its own process
even if the STA is blocked or the parent has died; the parent still confirms
owned-process exit with a one-second cleanup deadline. Graceful shutdown waits
for that independent supervisor, without keeping the monitoring owner running.
No ABI slots changed.

Tests use fake observations/native sessions and owned non-COM child faults, never
actual switches. Preserve PR 5 deferrals and PR 9 timing uncertainty. Native ABI,
real transitions, Narrator, mixed DPI and shell races require separately authorized
owner validation; automated passes cannot approve those claims.

## Implemented authorization and results

`MonitoringController.SwitchToWorkAsync` excludes overlapping requests. A short
`AuthorizeSwitch` command checks monitoring, interactive style, saved build-scoped
consent, roles and capability. Runtime authorization drains pending events and
requires the strictly confirmed nudge HWND to remain foreground in fresh healthy
Personal evidence, with its original shell/health generation. Destruction,
external foreground (including leave-and-return), desktop/session/power changes,
sequence loss, commands and reassignment revoke eligibility. It does not read
COM, reset the estimator or invoke a durable Core command. Runtime invalidation
also revokes any issued, unconsumed grant, including foreground leave-and-return,
provider failure/health loss, session changes and topology/shell changes. Host
commands, presentation closure and shutdown revoke outstanding grants too.
Revocation and single-use consumption compete through the same atomic exchange;
once either wins, the grant cannot become usable again. Consumed requests cannot
be retracted or reversed. One executor consumes a grant before its first await.
The original evidence timestamp, UTC uncertainty check and two-second uptime
lease remain binding across worker startup; no renewal occurs.

The worker independently captures exact build/architecture/session, active input
desktop, shell, foreground and ordered topology. Configuration, shell and foreground
are bracketed around native enumeration. After GUID resolution it captures again.
Post-call verification checks the same environment and topology, allowing the
ordinary foreground change associated with a desktop transition.

`ISwitchExecutor` returns ConfirmedSuccess, Rejected, VerifiedFailure or
Indeterminate. VerifiedFailure remains a reserved typed outcome; the current
worker classification does not emit it. `WrongDestinationOrUserRace` is
Indeterminate: an unexpected GUID cannot establish whether a user race occurred.
Its diagnostic category is retained and the native result still carries the
observed GUID. The nudge disables further attempts and directs Task View inspection.
Timeout, native exception after attempted invocation, uncertain environment,
malformed/missing/oversized reply, abnormal exit and failed cleanup are
Indeterminate. IPC rejects duplicate and unknown fields and missing required
constructor fields. Both streams and input are bounded at 65,536 characters.

The Dispatcher retains one shared submission reservation across switching,
extensions and dismissal. Every unsuccessful switch result is retained through
the existing local Home/Settings/tray feedback path even if monitoring has already
closed the originating nudge. Indeterminate disables that window's switch action;
extensions/dismissal remain available. No result opens or focuses a new window.

The host snapshot projects a fail-closed switching availability reason. The
primary action is disabled when consent is missing or normal application build
compatibility is blocked, with a polite accessibility announcement and Win+Tab
fallback. Known unavailable requests do not start progress feedback. Every actual
request still independently passes host and worker authorization.

An internal worker entry seam runs the production protocol, apartment and exact
build guards with a fake session factory in tests. Production still supplies only
the native factory and STA worker entry, under the existing process watchdog.

Source checks and SwitchDesktop cannot be atomic with user/shell actions. A
consent/context change after dispatch cannot cancel an accepted shell request.
The watchdog/parent kill cannot undo it either. Process termination and the pinned
ABI remain best-effort Windows mechanisms, not a shell-safety guarantee. Existing
Explorer recovery, sustained monitoring and owner acceptance deferrals remain.

## Changed files and interfaces

| Component | Implementation files |
|---|---|
| Shared assembly | `src/OfframpShift.Switching/OfframpShift.Switching.csproj`, extracted `Model.cs` / `NativeSession.cs`, new `Switching.cs` / `SwitchWorkerEntry.cs` |
| Spike extraction | Original Model/NativeSession source paths removed; `spikes/VirtualDesktop/Probe.Core/Probe.Core.csproj` references the shared assembly; probe README updated |
| Host | `HostSnapshot.cs` (commands/result grant), `MonitoringApplication.cs` (consent/authorization), `MonitoringRuntime.cs` (nudge lifetime), `MonitoringController.cs` (asynchronous supervision/shutdown), `WindowsApplication.cs` (configuration boundary), `WindowsSettings.cs` (strict migration), host project reference |
| WPF | `App.xaml.cs`, `Program.cs`, `MainWindow.xaml` / `.xaml.cs`, `NudgeWindow.xaml` / `.xaml.cs`, `NudgePresenter.cs` |
| Regression coverage | New host `SwitchRegressions.cs` / `SwitchPolicyRegressions.cs`; existing host fixture, entry point and migration regressions; WPF `InteractiveUiRegressions.cs` and entry point/watchdog test |
| Build/documentation | Root runner formatting list, ignored artifacts directory, transitive project-only lockfile updates in app/host/tests/observation probe, READMEs, architecture/qualification records and upstream notice link |

The narrow executor contract is `ISwitchExecutor.ExecuteAsync(SwitchAuthorization)`
returning `SwitchCompletion` / `SwitchOutcome`. `AuthorizedSwitch` carries the
role/request envelope; `SwitchAuthorization` provides atomic consume/
revoke. `SwitchWorkerRequest` / `SwitchWorkerReply` and `SwitchProtocol` validate
stdio. `IsolatedSwitchExecutor` owns normal execution; its raw diagnostic helper
and the native qualification constructor are internal to the shared assembly.
`SwitchCompatibility` keeps application and diagnostic predicates separate.
Core, Persistence, ActivityMonitoring, browser behavior and read-only observation
implementation files are unchanged.

The qualification command is structurally one-shot: it constructs a fresh request,
dispatches it once to a single-request worker and exits. Normal application grants
still use atomic `SwitchAuthorization.Consume`; qualification does not enable or
reuse a normal application grant. The standalone read-only harness can compare
one reading to an explicitly supplied existing GUID, without printing identities
or assigning roles. Owner corroboration in Task View remains required.

## Proposed 26300 build-family policy (inactive)

This is a design for a subsequent owner-approved patch, not an active predicate.
Preparation accepts exactly **26300.9711/X64** in standalone qualification, while
normal application switching remains exactly **26200.9457/X64**. Existing
26200.9711 diagnostic and read-only approvals remain unchanged. Production
read-only observation remains limited to the two approved 26200 exact builds.
See the [current minimal qualification procedure](../validation/pr10-explicit-switching.md#owner-qualification-separately-authorized-minimal-26300-procedure).

After the owner separately reviews and approves the completed smoke evidence,
explicit user-initiated switching may support the **26300 build family on x64**.
Parse a valid numeric build/UBR and require exact family 26300, not a string prefix;
263000, malformed versions, unknown architecture and other major build families
remain unsupported before COM. Routine UBR/cumulative updates within that approved
family should not need another approval. A different family requires separate
review and new consent. A known regression may disable support with a focused
fail-closed change; family approval cannot promise undocumented ABI stability.

Switching consent stays explicit, separate from observation consent and default
off. Propose a versioned settings migration with an explicit consent scope kind
and family (e.g. switching/family/26300/X64), rather than changing the meaning of
existing v4 `SwitchingConsentBuild`. Do not reinterpret old exact-build consent as
family consent. Migrate to family switching consent off; require a deliberate
new opt-in after capability approval. No downgrade fallback or parse failure may
grant consent. Observation approval/consent and GUID assignments need their own
scope and runtime checks; they must not be implicitly widened by this migration.

On future cumulative updates in approved 26300, retain that explicitly granted
family switching consent; fresh runtime validation and strict extant GUID bindings
still apply on every attempt. Currently the host's observation/settings build
review is exact-build and revokes switching consent. A later implementation must
review those comparisons together: preserve family switching consent through a
routine UBR review only if the observation path is separately approved and roles
remain valid, or keep the action unavailable. Never remove the observation gate
just to make switching available. Role reassignment, consent revocation and
uncertainty must continue to invalidate grants. On a different major build family,
invalidate consent and require fresh review/assignment validation and explicit
opt-in; returning to an older family must not silently resurrect a prior grant.
Keep actual build/UBR in runtime snapshots even when consent is family-scoped.

Family eligibility is only one condition. Every click still requires fresh,
single-use authorization, an unexpired lease and independent runtime/worker
validation of configuration, session, Personal source, Work GUID, foreground,
shell and topology. Keep isolation, at most one native call, bounded verification,
no retries, automatic rollback, window movement or unattended switching.
Indeterminate results retain nudge lockout and Task View/manual recovery. Source
checks and the shell call remain non-atomic; killing a worker cannot undo an
accepted transition. Automatic switching remains **NO-GO**.

Keep three independent policies: production read-only observation, standalone
diagnostic switching and normal application switching. The read-only diagnostic
also retains its own exact-build opt-in. Neither diagnostic PASS nor switching
family approval enables production observation or certifies sustained monitoring,
accounting, Explorer recovery, accessibility or the deferred PR 5 cases.
