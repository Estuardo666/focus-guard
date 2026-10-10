# FG-147 — Installer interactive Shell launch fix

## 1. Summary

Fixed the reinstall failure that opened the user's Documents window and then
reported that `FocusGuard.Desktop` did not remain running. The installer now
uses `Shell.Application.ShellExecute` in the interactive Explorer session
instead of passing an executable path to `explorer.exe` as a navigation target.

## 2. Files changed

- `scripts/install-focusguard.ps1`
- `scripts/test-installer.ps1`
- `TASKS.md`
- `docs/progress/20260802-1745-installer-shell-launch-fix-plan.md`
- this checkpoint
- regenerated installer directory and ZIP

## 3. Tests added

The installer contract now requires Shell automation execution and explicitly
rejects the broken `Start-Process -FilePath $explorer` implementation.

## 4. Commands executed

- `scripts/test-installer.ps1`
- `dotnet format FocusGuard.slnx --verify-no-changes --no-restore`
- `dotnet build FocusGuard.slnx -c Release --no-restore`
- `dotnet test FocusGuard.slnx -c Release --no-build --no-restore`
- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.0`
- packaged installer contract test
- graphical Setup smoke test

## 5. Results

- Release build: passed, 0 warnings and 0 errors;
- automated tests: 321 passed, 0 failed;
- installer contracts: passed;
- graphical smoke test: passed;
- ZIP SHA-256:
  `121DB93399BAD2A04C5F85FA15197B22827A0F048FF4221FA880F2AC813211A4`.

## 6. UX implications

Reinstall completion launches FocusGuard minimized and no longer opens an
unrelated Documents window.

## 7. Security implications

Interactive components remain launched by the logged-in user's Shell rather
than directly inheriting the elevated Setup token.

## 8. Recovery implications

The existing startup verification and rollback behavior remains intact. If an
interactive component genuinely exits during startup, the installer still
restores the previous installation instead of claiming success.

## 9. Limitations

The regenerated package passed static and smoke validation. The reported path
must be retested through an actual elevated reinstall to confirm behavior in
the owner's Explorer session.

## 10. Next task

Repeat the elevated reinstall with the regenerated ZIP and verify the service,
ProcessAgent, Desktop tray state, and Start Menu shortcut.

