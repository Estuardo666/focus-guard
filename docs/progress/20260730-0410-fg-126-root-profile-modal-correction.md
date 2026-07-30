# FG-126 correction — Root-level profile modal

## Summary

Corrected the profile creator overlay boundary. The previous implementation lived inside `ApplicationShell`, so it covered only the Profiles content area and still appeared inline. It now sits beside `ApplicationShell` at the root window level, matching the Strict Mode modal structure.

## Functional result

- The scrim covers the entire FocusGuard window, including header and sidebar.
- The underlying shell is blurred and cannot receive pointer interaction.
- Keyboard focus moves to the profile name field.
- Tab navigation remains trapped in the creator.
- Escape and Cancel close the modal and restore the previous focus target.
- Profile validation and persistence behavior are unchanged.

## Validation

- Root-level modal and guided-profile tests: 7 passed.
- Full .NET suite: 252 passed.
- Browser extension suite: 14 passed on verification rerun.
- Release build: 0 warnings and 0 errors.
- Formatting verification: passed.

## Test note

The first full extension run measured one performance case at 10.137 ms against its 10 ms median threshold. An isolated verification rerun passed all 14 extension tests; no extension code changed in this checkpoint.
