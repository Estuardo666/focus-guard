# FG-107 installer and uninstall recovery

## Scope

This checkpoint adds a production `win-x64` bundle and explicit, elevated
PowerShell install/uninstall procedures. It does not execute installation,
modify SCM, change ACLs, or delete machine state in unattended work.

## Acceptance criteria

- Production publish uses the `FocusGuardProduction` build profile and names
  the service `FocusGuard`.
- The bundle contains the service, desktop, recovery utility, and a manifest.
- Installation stages files under `C:\Program Files\FocusGuard`, creates
  `%ProgramData%\FocusGuard`, registers the service as `LocalService`, and
  applies restrictive ACLs. SYSTEM and Administrators retain full control for
  servicing and rollback; `LocalService` receives read/execute access to the
  binaries and modify access only to machine state.
- Installation snapshots existing binaries and ACLs before mutation. Any
  staging, ACL, SCM, description, or startup failure removes a newly created
  service and restores the prior files, permissions, and running state.
- Uninstall refuses while active or pending strict state exists and reports
  the recovery procedure with no files removed.
- Normal uninstall removes the service and binaries, preserves machine state
  by default, and requires `-PurgeData` for explicit data removal.

## Owner-present drill

1. Build with `scripts\build-installer.ps1`.
2. On a disposable Windows machine, run the installer elevated and record SCM,
   binary path, account, and ACLs.
3. Inject a controlled startup failure and verify automatic rollback restores
   the exact prior files, ACLs, service registration, and running state.
4. Arm a finite synthetic strict policy, confirm uninstall refuses with no
   changes, then run recovery and wait for release.
5. Retry uninstall and verify service removal, binary removal, preserved state,
   and explicit `-PurgeData` behavior.

## Security, UX, and recovery implications

The exact release path is recovery-first: an active policy cannot be silently
removed by the installer. The recovery executable is staged beside the service
and is independent of the desktop. Both scripts require an existing
administrator token and never automate UAC or request credentials.
