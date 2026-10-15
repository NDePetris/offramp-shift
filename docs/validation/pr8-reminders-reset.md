# PR 8 — break reminders and manual usage reset

This owner-evaluation change adds default-off break reminders and a confirmed
Settings-only usage reset. Notifications reuse native `NotifyIcon` balloons and
Core grace/repeat cadence. Checkpoint schema remains version 2; Windows settings
strictly migrate v1 to v2 in memory and write v2 on the next explicit atomic save.
No monitoring configuration is changed when toggling reminders.

## Evidence and failure boundaries

The owner approved best-effort delivery: reminders are fresh and eligible when
requested, but Windows may suppress or display them late. A successful request
does not establish display. Reminder clicks never activate error details; retained
errors take priority. Only one balloon request is outstanding until a close/click
callback; additional notifications are discarded. When a reminder balloon is
outstanding, a new tray error is
retained in **Action needs attention…** without replacing that balloon. Native
callbacks carry no per-message identity; quiet/suppressed balloons may not report
their lifecycle, and the retained menu remains the dependable error path.

Automated tests cover reset domain state and continuity, configuration/Pause/effect
sequence preservation, durable round-trip, failed saves/rollback and acknowledgment
loss with exact retry, strict v1/v2 settings validation/migration/atomic failure,
initial/repeat nudges, default-off preference, consumption/expiry, stale and
ineligible context suppression, post-commit signal/age rechecks, retry/restart/reset
discarding, notification collision/click policy and affected native WPF controls.
They use synthetic providers, local disposable SQLite and owned non-COM workers.
They do not establish live Windows notification display, focus, shell reliability,
accounting accuracy or Narrator acceptance.

Automated checks on October 15, 2026:

| Check | Result |
|---|---|
| `./run.ps1 test` | **PASS** — 75 Core, 70 Persistence, 59 host and 20 app groups; zero build warnings/errors |
| `./run.ps1 format` | **PASS** — established builds and formatting checks |
| `./spikes/VirtualDesktop/run.ps1 test` | **PASS** — 58 fake-provider cases and three owned non-COM worker checks; no switches |
| `./spikes/BrowserAttribution/run.ps1 test` | **PASS** — 2,053 policy/protocol assertions, five mocked extension tests and synthetic transport |
| `./spikes/WindowsObservation/run.ps1 test` | **PASS** — 41 synthetic/local-SQLite groups |
| `./spikes/WindowsObservation/compatibility.ps1 test` | **PASS** — 14 synthetic/owned non-COM groups |
| `./spikes/WindowsObservation/run.ps1 format` | **PASS** |
| App tests `--render` | **PASS** — 16 synthetic captures generated; four new reminder Settings views reviewed in light/dark at normal/minimum sizes |
| Scoped Impeccable detector and `git diff --check` | **PASS** — no findings |

The local application scripts used `UseArtifactsOutput=true` and an ignored
`.tools/pr8-artifacts` output directory because existing app processes held the
normal output files open. Those processes were left running. No harness changes
were needed; CI uses the ordinary scripts on clean Windows runners. CI results
are recorded in the implementation PR. Live owner smoke is **NOT RUN** until
separately reported. No new manual PASS is claimed.

## Short owner smoke procedure

Record tested commit, exact Windows build/architecture and display scaling, then
PASS/FAIL/NOT RUN per item. Use an already configured owner account on an approved
read-only build. Note original duration settings before temporarily shortening
the allowance; duration edits clear an unfinished break. Restore those durations
after the smoke. The default grace period is one minute of eligible Personal
usage, not one minute of elapsed time. No extended repeat-cadence run is needed.

1. In Settings, enable **Break reminders**, select **Save reminder preference**,
   and verify the preference remains saved when reopening Settings.
2. With a temporarily short allowance, use Personal actively through exhaustion
   and the existing grace period. Observe the first **Time for a break** balloon
   if Windows permits it; the app must not activate its main window or steal focus.
   Record suppression separately from a successful request; display is best effort.
3. Select **Reset usage…**, review the loss confirmation and confirm. Expect saved
   usage and break progress to clear, allowance available, and Pause/settings
   preserved. Reset is independent of **Recover accounting…**.
4. Disable reminders, save, exit through the tray and relaunch. Verify the preference
   remains off. Restore any temporarily changed durations.
5. Use the existing safe duplicate-assignment rejection if needed (assign Work to
   the already assigned Personal desktop). Verify **Action needs attention…** still
   opens usable error details; a reminder click must never open/dismiss them.

No Explorer restart, normal-data corruption, extended reminder run or broad
accounting campaign is requested. PR 5 Tests 8–12 remain deferred / **NOT RUN**;
Explorer recovery (#8) and sustained accuracy/reliability (#9) remain unresolved.
Automatic switching/window movement and production browser attribution remain
**NO-GO**. Build allowlists, permissions, local-only privacy boundaries and
undecided licensing/distribution are unchanged.
