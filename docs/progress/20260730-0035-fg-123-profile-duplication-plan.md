# FG-123 Profile Duplication Plan

## Summary

Implement the smallest missing profile-management slice from MVP user story 1:
duplicate the selected profile, including its application and website rules.

## Relevant requirements

- Profiles are reusable collections of enforcement rules.
- Profile editing must not mutate an immutable active-session snapshot.
- UI strings must use English and Spanish resources.
- Accessibility and visible command state are acceptance criteria.
- Business and storage logic must remain independent of Avalonia.

## Scope

- Add a localized `Duplicate profile` command to the Profiles screen.
- Create a new profile ID and new globally unique IDs for every copied rule.
- Preserve the source description, accent, actions, match kinds, and patterns.
- Generate a localized, collision-free copy name.
- Persist and select the duplicate immediately.
- Disable duplication during an active session or while another operation runs.

Rename and delete remain separate checkpoints so destructive confirmation and
validation can be reviewed independently.

## Tests first

1. Duplicating creates and selects a second persisted profile.
2. All copied rule values remain equivalent but receive new IDs.
3. Repeated duplication produces a collision-free name.
4. Active sessions disable and refuse duplication.
5. English and Spanish resources contain the new user-facing copy.

## Security implications

The duplicate affects future sessions only. It never replaces the policy
snapshot of a running session. New rule IDs are required because the SQLite
schema uses rule ID as a global primary key.

## UX implications

The action is visible beside the profile selector, uses localized text and an
accessible name, selects the new copy after success, and explains that it is
ready to edit.

## Recovery implications

Duplication is additive and non-destructive. If persistence fails, the original
profile remains unchanged and the incomplete copy is not selected.
