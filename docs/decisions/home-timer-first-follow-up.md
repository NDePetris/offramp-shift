# Home timer-first follow-up — deferred beyond PR 9

Owner requirements captured October 16, 2026. Recommended tracking: a dedicated
subsequent GitHub issue and cohesive Home UI PR. This is not PR 9 implementation
scope and does not approve a new timing/accounting model.

- Before exhaustion, prominently show **Personal time remaining**.
- After exhaustion, show **Over allowance**; total accumulated Personal usage is
  secondary information, rather than an awkward used-of-allowance primary label.
- Show a separate **Next reminder** countdown from Core's remaining eligible
  Personal time. During an explicit extension, label it **Extra Personal time remaining**.
- Distinguish active, paused, Work, idle and unavailable states without extrapolating
  unobserved usage or advancing an independent UI timer.
- Keep qualifying-break progress visible.
- Move technical monitoring/recovery details below the primary visual hierarchy,
  retaining accessible diagnostics and explicit recovery actions.
- Prefer a shared presentation projection for Home and the interactive nudge;
  Core remains the sole timing authority. Reuse committed snapshot remainders.

Future acceptance should cover allowance/exhaustion, each extension, ordinary
repeat cadence, paused/Work/idle/unavailable states, break progress and keyboard/
screen-reader hierarchy. PR 10 switching and issue #17 native notifications remain
separate. No Home UI controls or layout are changed by this capture.
