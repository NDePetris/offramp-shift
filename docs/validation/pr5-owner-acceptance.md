# PR #11 integrated Windows acceptance

## Owner acceptance record — October 15, 2026

Tested implementation commit: **`80f027b4c6731529b81f1abd147ed10ec9745348`**.
The owner explicitly reported **PASS for Tests 1–7**, and explicitly elected to
defer live **Tests 8–12 for the initial owner-evaluation milestone**, accepting
the documented limitations and risks. Each deferred test is **NOT RUN**, not
PASS. This is a scope decision, not a waiver of automated regression requirements
or evidence that the deferred behavior works.

| Test | Exercise | Live result | Evidence / decision |
|---|---|---|---|
| 1 | Clean Exit and worker disposal | **PASS — owner-reported** | Report applies to the tested commit; no agent-executed live verification |
| 2 | Setup and restoration | **PASS — owner-reported** | Same evidence boundary |
| 3 | Persisted Pause | **PASS — owner-reported** | Same evidence boundary |
| 4 | Tray lifecycle | **PASS — owner-reported** | Same evidence boundary |
| 5 | Keyboard, Narrator and contrast | **PASS — owner-reported** | Overall test result; no separate per-variant measurements supplied |
| 6 | Lock and resume | **PASS — owner-reported** | Bounded acceptance, not independent sustained accounting certification |
| 7 | Context and feedback | **PASS — owner-reported** | Existing advisory behavior only |
| 8 | Rename/reorder/delete | **NOT RUN — owner-deferred** | Deferred topology-mutation acceptance |
| 9 | Explorer + explicit Retry | **NOT RUN — owner-deferred** | Recovery remains unvalidated on this integrated commit; issue #8 remains open |
| 10 | Storage failures and exact Retry save | **NOT RUN — owner-deferred** | Automated fault tests remain required; live UI fault acceptance deferred |
| 11 | Corrupt/future/blocked data | **NOT RUN — owner-deferred** | Automated storage/recovery tests remain required; live acceptance deferred |
| 12 | Crash/logout | **NOT RUN — owner-deferred** | Crash/native-call cleanup and logout acceptance deferred |

Source: the owner's explicit report and deferral in the PR #11 closeout chat.
No new runtime/hardware/privilege transcript, timings, logs or per-variant results
were supplied with this report. The previously supplied host is documented below
as context, not a newly measured environment. Do not extend these results to
other builds/hardware or relabel them as agent verification. Later documentation
commits retain this exact tested implementation revision as the live provenance.

The original broader acceptance criteria remain below for future execution.
The initial owner-evaluation scope is accepted with Tests 8–12 deferred; it does
not establish full original acceptance, certified accounting, production readiness
or unattended daily use. Issues #8/#9 remain open. Existing delayed/failed-cleanup,
blocked native-call crash cleanup, Explorer recovery and sustained accuracy
limitations remain in force. Switching and production browser attribution remain
NO-GO. See [the final review verdict](pr5-app.md#final-owner-evaluation-closeout--october-15-2026).

## Procedure and environment record

Use ordinary unelevated PowerShell on exactly 26200.9457/x64 or 26200.9711/x64.
Save other work. Record date, tested commit, Windows edition/release/build,
runtime, privileges, CPU/RAM, storage/filesystem, power mode and monitor
resolution/scaling/layout. The previously reported host was Windows 11 25H2
Professional 26200.9711/x64, two side-by-side displays (2560x1440 at 150%,
1920x1080 at 100%); re-confirm rather than copying that description blindly.

```powershell
Set-Location G:\Projects\offramp-shift
git branch --show-current # Require feat/first-usable-windows-app
git rev-parse HEAD        # Record the integrated PR head being tested
./run.ps1 app             # Pinned SDK; locked restore and Release build, then launch
```

Exit any older app through its tray first, so the singleton cannot redirect this
launch. The app writes normal `%LOCALAPPDATA%\Offramp\Shift` settings/checkpoints;
it has no supported test-directory CLI override. For a fresh/isolated store use a
separate standard Windows test account with repository/runtime access. Preserve
normal data while the app is stopped; do not delete or replace it to run a check.

## Original priority check criteria — Tests 1–7

| Check | Owner actions and pass criteria |
|---|---|
| 1. Clean Exit and worker disposal | Launch, then choose **tray Exit**. Confirm both the interactive app and its owned `--desktop-worker` exit in Task Manager (inspect locally; do not return process lists). A delayed/failed-cleanup message or remaining helper is FAIL, not successful shutdown. Do not terminate unrelated dotnet processes. Repeat once after monitoring is enabled. |
| 2. Setup and restoration | Explicitly consent if required. Select Personal/Work yourself in Task View and assign each through the tray; duplicate assignment must reject. Start. Record durations and saved totals, Exit and relaunch. Require same assignments/durations/totals, fresh observations before accounting, and no credit for closed-app time. Saved enabled/unpaused intent may resume only with valid prerequisites. |
| 3. Persisted Pause | Pause, record totals, Exit, wait, relaunch. Require Paused and unchanged totals while paused. Resume explicitly; fresh evidence must precede new progress. No downtime credit or replay. |
| 4. Tray lifecycle | Close the window: verify the first-close explanation/choice, close-to-tray, tray reopen and saved close preference. Launch again while running: one interactive owner, existing window opens, no second accounting owner (an owned worker is expected). Exit must remove the tray icon and dispose workers. |
| 5. Keyboard, Narrator and contrast | Use Tab/Shift+Tab/access keys for Home/Settings and Windows tray keyboard access. Verify visible focus, usable labels/status announcements, all essential controls, light/dark/system and high contrast. Resize/enlarge text; inspect both mixed-DPI displays and 100/150/200% where available. Record individual variants NOT RUN when unavailable. Restore display/accessibility settings afterward. |
| 6. Lock and resume | Record saved totals; lock/unlock, then separately sleep/resume with work saved. Require retained totals, fresh observations and no accounting across uncertain gaps. Paused state must persist. A healthy dashboard alone cannot establish interval accuracy. |
| 7. Context and feedback | Manually inspect Personal/Work, then Other on a disposable third desktop; verify unavailable/Unknown never looks earned and stale display does not advance by wall clock. In isolated test data use short valid durations to observe saved usage/break, reset, exhaustion/grace/nudging feedback; these are advisory states, not active interventions. Restore durations explicitly. |

Passive video with no input may currently earn break credit after idle
qualification on Personal. Record it as current policy, not an observer defect;
cumulative-break or media-aware policy changes are separate work.

## Deferred criteria — Tests 8–12, isolated data and saved work required

The five exercises below map to Tests 8–12 in table order. All five are **NOT RUN**
under the explicit owner deferral above; retain their criteria for future work.

Use the separate standard test account and disposable desktops/store only. Do not
corrupt a live database, change the system clock, delete a desktop containing
unsaved work, or kill Explorer on the ordinary working session to fill this table.

| Exercise | Action / expected result |
|---|---|
| Rename/reorder/delete | With disposable assigned desktops, rename/reorder in Task View: roles retain GUID identity. Delete an empty assigned desktop: unavailable; recreating the name does not repair it. Reassign explicitly. Never issue an app-driven switch. |
| Explorer + explicit Retry | Save all work in the test session; deliberately restart Explorer through Task Manager. Record Unavailable if recovery fails. After settling, use **Retry monitoring**, honoring cooldown; fresh baselines, retained totals, no gap credit and no leaked owned helpers. Failure remains recorded; a later retry is a separate result, not automatic recovery or closure of #8. |
| Storage failures and exact Retry save | Use a reviewed controlled fault fixture against disposable test data to make a duration save fail. UI must stop accounting, report PendingCommit and offer **Retry save** for the exact pending checkpoint. Verify acknowledged durations after retry, no duplicate effects, responsive UI and pending-save Exit choices (cancel/retry/explicit possible loss). Do not improvise ACL/disk faults on normal data. If no controlled UI fixture is available, mark NOT RUN. Automated injected-fault tests are separate evidence. |
| Corrupt/future/blocked data | With the app stopped and reviewed disposable fixture data, check RecoveryRequired: explicit confirmation preserves originals before reset. Incompatible or clock-blocked storage must never offer destructive recovery. Invalid Windows settings repair preserves the original and does not reset accounting. Do not edit SQLite bytes or roll back the real clock; without suitable fixtures mark NOT RUN. |
| Crash/logout | Test account only, work saved: record committed totals, deliberately terminate only the interactive app, inspect owned-worker cleanup, relaunch and verify committed restoration without downtime credit/effect replay. Separately sign out/in and revalidate. Blocked native-call crash cleanup is a documented limitation; do not claim a clean exit from a crash. |

Return an anonymous row per check/variant: **PASS / FAIL / BLOCKED / NOT RUN**,
tested SHA/environment, expected/observed result and cleanup outcome. Retain failed
attempts separately from deliberate retries. No GUIDs, process lists, titles,
URLs, app keys or activity histories. Unknown gaps are not earned time. Automated
passes do not complete this table. Issues #8/#9 and broader reliability gates
remain open; no merge, release, deployment or licensing decision is implied.
