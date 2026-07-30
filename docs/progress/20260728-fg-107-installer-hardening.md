# FG-107 installer hardening checkpoint

## Summary

Granted the production `LocalService` identity explicit read/execute access to
the installation tree and added automatic rollback around every mutating
installer step.

## Files changed

- `scripts/install-focusguard.ps1`
- `scripts/test-installer.ps1`
- `docs/owner-present/fg-107-installer-uninstall-recovery.md`
- `docs/progress/20260728-fg-107.md`
- this checkpoint

## Tests added

- Static contracts require the LocalService binary ACL, temporary backup,
  prior-ACL restoration, rollback warning, and combined failure reporting.

## Commands and results

- PowerShell parser and installer contract tests passed.
- Install and uninstall `-WhatIf` simulations passed without mutation.
- `dotnet format --verify-no-changes` passed.
- Release build passed with 0 warnings and 0 errors.
- All 119 .NET tests passed.
- All 6 extension tests passed.

## Owner-present production installation

The owner explicitly authorized installation on 2026-07-28. Read-only preflight
confirmed an elevated token, valid package and manifest, no pre-existing
production service, and no active policy or pending recovery request.

- `FocusGuard` was created successfully and is `Running` / `Automatic`.
- SCM account is `NT AUTHORITY\LocalService`.
- Process path is
  `C:\Program Files\FocusGuard\FocusGuard.Service.exe`.
- Installed service SHA-256 matched the published bundle:
  `F1F71D0459CB82FA84498207086D6ED16BD4BDAAC55E3BB0D4F56D3FB21BB7CF`.
- Recovery and desktop executables are present.
- Install ACL: SYSTEM and Administrators full control; LocalService
  read/execute.
- Machine-state ACL: SYSTEM and Administrators full; LocalService modify.
- A service restart returned to `Running` with a new PID.
- The elevated recovery utility executed and returned expected exit code 4
  because no recovery state has been provisioned.
- `FocusGuard.Development` remained running with manual start.
- No strict policy or pending recovery request was created.

## UX implications

An installation failure now reports that the previous FocusGuard state is being
restored. If rollback itself fails, the error includes both failures.

## Security and recovery implications

LocalService can execute installed binaries but cannot modify them. Existing
machine state remains writable by LocalService. Rollback removes only the exact
FocusGuard installation/data roots created by the failed attempt and restores
pre-existing files and ACLs from a uniquely named temporary backup.

## Limitations

SCM, ACL, binary identity, service restart, and inactive recovery execution are
now verified. Automatic rollback under an intentionally injected failure and
active-session uninstall refusal remain untested on this machine.

## Next task

Connect production activation before any real strict-session drill; test
failure rollback and active-session uninstall refusal only in a disposable
environment.
