# FG-146 — Installer runtime restart and shortcut access

## 1. Summary

Corrected the reinstall completion path so setup no longer reports success
until the FocusGuard service, ProcessAgent, and Desktop are running. Fixed the
Explorer shortcut access failure by granting built-in Users read/execute access
to installed binaries and refreshing the owned Start Menu shortcut on every
installation.

## 2. Files changed

- `scripts/install-focusguard.ps1`
- `scripts/test-installer.ps1`
- `TASKS.md`
- `docs/progress/20260802-1630-installer-runtime-restart-plan.md`
- this checkpoint
- regenerated `artifacts/installer/FocusGuard-0.1.0/`
- regenerated `artifacts/installer/FocusGuard-0.1.0-win-x64.zip`
- refreshed `graphify-out/`

## 3. Tests added

Extended the installer contract test to require:

- built-in Users read/execute ACL provisioning;
- interactive process launch through Explorer rather than the elevated Setup
  token;
- ProcessAgent and Desktop startup verification;
- refresh of the shortcut icon to the installed Desktop executable;
- a final runtime-ready message only after all startup checks pass.

## 4. Commands executed

- `scripts/test-installer.ps1`
- `dotnet build src/FocusGuard.Setup/FocusGuard.Setup.csproj -c Release --no-restore`
- `dotnet format FocusGuard.slnx --verify-no-changes --no-restore`
- `dotnet build FocusGuard.slnx -c Release --no-restore`
- `dotnet test FocusGuard.slnx -c Release --no-build --no-restore`
- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.0`
- `scripts/test-installer.ps1 -PackageDirectory artifacts/installer/FocusGuard-0.1.0`
- `artifacts/installer/FocusGuard-0.1.0/FocusGuard.Setup.exe --smoke-test`
- `graphify update .`

## 5. Results

- formatting: passed;
- Release build: passed with 0 warnings and 0 errors;
- automated tests: 321 passed, 0 failed;
- installer source and packaged-contract tests: passed;
- graphical Setup smoke test: passed;
- package SHA-256:
  `5E3EDA6C77F6CEE5918736967B25C419D2E99F499B6A415F37271D7EABA6226D`.

## 6. UX implications

The completion screen now means the full runtime is ready. Desktop is restored
minimized, so reinstall does not unexpectedly cover the installer, and the
shortcut remains usable from the normal Explorer session.

## 7. Security implications

Built-in Users receive only inherited read/execute rights under Program Files;
write and full-control permissions remain restricted to SYSTEM and
Administrators. ProcessAgent and Desktop are delegated to the interactive
Explorer shell so they do not inherit Setup's administrator token.

## 8. Recovery implications and limitations

Any required startup failure enters the existing verified rollback path rather
than showing success. This pass did not execute an elevated reinstall against
the local machine, so the owner-present mutation drill remains necessary to
confirm the actual interactive token, process state, shortcut launch, service
state, and rollback behavior.

## 9. Next task

Run the owner-present elevated reinstall drill using the regenerated package,
then verify service, ProcessAgent, Desktop, shortcut launch, and controlled
rollback before completing FG-145.

