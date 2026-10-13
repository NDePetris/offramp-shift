## Purpose and scope

What changes, why, and what is explicitly out of scope?

## Validation

- [ ] Build and applicable automated tests pass (include command and result)
- [ ] Existing regression/failure-path coverage updated where behavior changes
- [ ] Manual Windows/browser validation described with exact OS/build and configuration, or marked **not performed**
- [ ] Documentation updated where necessary

## Safety and privacy

- Does this change touch OS-specific/undocumented interfaces, switching, background intervention, browser attribution, permissions, or collected data?
- If yes: what can go wrong, what is the safe fallback, and what evidence supports it?
- State relevant **GO / NO-GO** decision gates. A passing CI run is not permission to lift a gate.

## Review notes

State assumptions, known limitations, follow-up work, and any blockers. Avoid implying simulation results prove live OS behavior.
