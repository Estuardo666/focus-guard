# FG-125 Profile Delete Plan

## Summary

Complete MVP profile management with persistent deletion that requires explicit
confirmation and preserves a usable fallback profile.

## Relevant requirements

- Destructive actions must show consequences before execution.
- Active immutable policies must not change when saved profiles are edited.
- Profile storage already deletes rules transactionally through foreign-key
  cascade.
- UI strings must use English and Spanish resources.
- Accessibility and recovery behavior are acceptance criteria.

## Scope

- Add a localized Delete action to the Profiles screen.
- First click opens an in-window confirmation showing the selected profile name
  and counts of application and website rules that will be removed.
- Provide explicit Cancel and final Delete controls.
- Prevent deletion while a session is active or another operation is busy.
- Prevent deleting the last remaining profile.
- After deletion, persistently select a remaining profile.
- Clear stale confirmation whenever selection or session state changes.

## Tests first

1. Requesting deletion shows consequences without changing storage.
2. Cancel closes confirmation without deleting.
3. Confirm deletes only the selected profile and selects a remaining profile.
4. The last profile cannot be deleted.
5. Active sessions disable and refuse request and confirmation.
6. English and Spanish resources contain the destructive copy.

## Security implications

Deletion changes saved configuration for future sessions only. It cannot modify
or weaken an active enforcement snapshot.

## UX implications

The destructive action uses the existing danger style and requires two
deliberate clicks. The confirmation names the profile, states its rule counts,
and says the operation cannot be undone.

## Recovery implications

At least one profile always remains. Cancellation is non-mutating. SQLite
foreign-key cascade removes only rules owned by the confirmed profile.
