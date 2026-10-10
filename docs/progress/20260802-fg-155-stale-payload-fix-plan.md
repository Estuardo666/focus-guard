# FG-155 — Stale embedded Desktop payload recovery plan

## Evidence

- The running Desktop process started at 18:32, before the UI changes.
- The installed `FocusGuard.Desktop.dll` does not contain the `RingDigitsPanel` UI marker.
- The 0.1.3 publish output does contain that marker and has a different SHA-256.
- Therefore 0.1.3 embedded and installed a stale Desktop payload even though the former self-test passed.

## Plan

1. Give each installer version a distinct payload input path so MSBuild cannot reuse the previous embedded resource.
2. Add a build-time receipt that hashes the Desktop DLL inside the standalone executable and compares it with the just-published DLL.
3. Build and validate 0.1.4.
4. Run the elevated owner-present installer, verify installed/published hashes match, and verify the restarted process timestamp.
5. Inspect the installed UI and only then close FG-153/154/155.

## Security, UX, and recovery implications

- Security: the receipt accepts only a canonical path contained under the extracted package root.
- UX: the fix prevents an installer from silently delivering an earlier UI.
- Recovery: the existing transactional installer backup and rollback remain authoritative during the owner-present upgrade.
