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
- Original WindowsSettings v4 added exact-build consent. Current v5 adds explicit family consent, default off. Strict v1–v4
  migration preserves reminder/appearance/assignment preferences; SQLite is untouched.
- NudgeWindow/Presenter add a primary explicit action, shared submission exclusion,
  progress and typed feedback, retaining extension/dismissal behavior. MainWindow
  Settings exposes consent and current capability restrictions.
- New host/WPF/policy regressions and a dedicated owner qualification checklist
  cover failure boundaries and retain all seven established checks.

## Safety and validation plan

No automatic switching, ordinal fallback, retries, rollback, input injection or
window movement. Capability and exact runtime-configuration checks run before
native activation. Normal application eligibility accepts 26200.9457/x64 and the
numeric 26300.UBR family on x64 for controlled owner evaluation, with separate
explicit consent. 26200.9711/x64 remains observation/diagnostic-only. Switching and
observation have independent owner decisions; neither consent grants the other.

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
sequence loss, commands and reassignment revoke eligibility. If the confirmed
nudge has just acquired foreground but the published observation still names the
previous application, authorization takes one fresh read-only capture through the
existing bounded observation worker. Pending events are drained before and after;
the original shell/health generation and all context checks still apply. It does
not reset or advance the estimator, renew a grant or invoke a durable Core command.
The COM reply wait has a two-second deadline and worker cleanup has a one-second
exit wait; collection over 500 ms is rejected. Synchronous local OS queries and
pipe writes are outside that reply timeout, so the full capture has no absolute
wall-clock deadline. This existing provider limitation can delay the monitoring
owner; the Dispatcher remains asynchronous. Runtime invalidation
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
Core, Persistence, ActivityMonitoring and browser implementation files remain
unchanged. The 26H2 patch changes read-only compatibility/consent scope and OS
architecture entry guards; its COM declarations and observation validation are unchanged.

The qualification command is structurally one-shot: it constructs a fresh request,
dispatches it once to a single-request worker and exits. Normal application grants
still use atomic `SwitchAuthorization.Consume`; qualification does not enable or
reuse a normal application grant. The standalone read-only harness can compare
one reading to an explicitly supplied existing GUID, without printing identities
or assigning roles. Owner corroboration in Task View remains required.

## Active 26300-family policy — controlled owner evaluation

The owner reported two expected-GUID read-only checks and one visually correct
Personal → Work native switch on **26300.9711/x64**, then expressly accepted this
limited evidence for integrated, opt-in testing. No additional standalone switch
repetitions are required before the integrated test. See the
[evidence and integrated procedure](../validation/pr10-integrated-26300.md).
This decision does not certify sustained monitoring, recovery, accessibility,
accounting accuracy, every UBR or unattended use. Automatic switching remains NO-GO.

Production read-only observation accepts exact 26200.9457/X64 and 26200.9711/X64,
plus family 26300/X64. Application switching accepts exact 26200.9457/X64 plus
family 26300/X64. Both parse a two-component `26300.<unsigned numeric UBR>`;
malformed revisions, bare/prefix families and different families reject. No new
approval is needed for routine UBR changes within 26300. Native entry guards also
require an x64 OS and process, excluding x64 emulation on ARM64. Independent worker
and native checks still precede undocumented COM activation. Actual build/UBR
remains in every runtime snapshot and must remain equal throughout one attempt.

Standalone switching qualification remains exact 26200.9711/X64 or
26300.9711/X64; the read-only diagnostic retains its own exact opt-in/release gate.
The original VirtualDesktop spike's parameterless native session still requires
`Capabilities.MayProbe` (26200.9457/X64). Expanding application support does not
expand that historical spike gate or the standalone qualification predicates.

WindowsSettings **v5** adds required `SwitchingConsentFamily`. It is blank by
default. When the owner explicitly checks the existing switching checkbox and
saves on 26300, `SwitchingConsent=true`, `SwitchingConsentFamily="26300"` and
`SwitchingConsentBuild=""`. Exact-build consent uses the existing build field and
an empty family field; the two scopes cannot coexist. Disabled consent requires
both fields empty. Exact consent cannot masquerade as family consent on 26300.
Both roles and monitoring consent must already be present; every attempt still
requires current healthy GUID membership and authorization.

Valid v1–v4 settings migrate in memory to v5 with switching **off**, preserving
other preferences, assignments, monitoring intent and reminder style. Old exact
switching consent never transfers, including old exact consent for 26300.9711.
Malformed v4 consent rejects before migration; migration does not launder invalid
values. Loading alone does not rewrite settings. The next explicit save writes
v5 atomically. Older applications treat v5 as incompatible; do not hand-edit or
use an old binary to downgrade it. Core/checkpoint schemas are unchanged.

Monitoring/observation consent stays independent. The host accepts saved/current
26300 build pairs in the same observation consent scope, so routine cumulative
updates require neither another setup approval nor another switching opt-in.
The saved Build remains the full build at consent, rather than losing UBR evidence.
Different families and the old distinct 26200 approvals still require explicit
observation setup review. Fresh source, topology, session and GUID validation
always applies; saved role names or indices never substitute for missing GUIDs.

On startup in a different switching consent scope, the host atomically clears the
switching grant before opening the accounting session. Failure to save revocation
aborts startup; returning to the old family cannot silently resurrect a grant
that was cleared. Explicit observation review and any role reassignment also clear
both switching scope fields. Unchecking **Allow an explicit Switch to Work attempt
from a nudge** and clicking **Save switching consent** clears them and revokes an
outstanding unconsumed grant. Tray **Pause** and **Exit** are additional immediate
controls; none can undo a request Explorer already accepted.

Every switch still passes fresh Personal/nudge foreground checks, GUID bindings,
a short single-use authorization, exact runtime configuration equality, bounded
STA isolation, one native call and postcondition verification. No retries, rollback,
window relocation, input injection or automatic/unattended switching were added.
Indeterminate completion retains its nudge lockout and Task View/manual recovery.
Consent/context changes cannot retract an already consumed shell request.
