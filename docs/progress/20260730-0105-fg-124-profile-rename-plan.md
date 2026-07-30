# FG-124 Profile Rename Plan

## Summary

Complete the next profile-management slice from MVP user story 1: rename the
selected profile with inline validation and persistent storage.

## Relevant requirements

- Profiles are immutable domain values persisted through `IProfileStore`.
- Profile names are required, trimmed, and limited to 80 characters.
- UI strings must use English and Spanish resources.
- Active-session policy snapshots must not change when profiles are edited.
- Accessibility and recovery behavior are acceptance criteria.

## Scope

- Move reusable profile-name validation into the Domain layer.
- Add a localized rename field, validation message, and command to Profiles.
- Reject empty, overlong, unchanged, and duplicate names.
- Persist the normalized name without changing the profile ID or any rule.
- Keep the renamed profile selected.
- Disable and refuse rename during active sessions or busy operations.

Profile deletion remains a separate destructive checkpoint.

## Tests first

1. Domain validation trims valid names and rejects empty or overlong names.
2. Rename persists, remains selected, and preserves all rules and metadata.
3. Empty, overlong, unchanged, and duplicate names cannot be submitted.
4. Active sessions disable and refuse rename.
5. English and Spanish resources contain the new user-facing copy.

## Security implications

Rename changes future profile metadata only. It does not replace or mutate an
active immutable enforcement policy.

## UX implications

The Profiles screen exposes a clearly labelled name field and localized Rename
button. Validation appears before submission, and success copy confirms that
the renamed profile is ready for future sessions.

## Recovery implications

The storage write is an upsert using the existing profile ID. On failure, the
existing stored profile remains the recovery source and is not removed.
