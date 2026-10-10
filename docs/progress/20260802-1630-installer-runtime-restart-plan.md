# Installer runtime restart and shortcut access — plan

## Scope

Fix reinstall completion so the graphical installer restores the FocusGuard
runtime before reporting success, and ensure the Start Menu shortcut is usable
from the ordinary interactive user token.

## Plan and acceptance criteria

1. Preserve the existing close, backup, rollback, service, and strict-recovery
   behavior.
2. Grant built-in Users read/execute access to installed binaries while keeping
   write access restricted to SYSTEM and Administrators.
3. Refresh the owned Start Menu shortcut on every install.
4. Start the service, ProcessAgent, and Desktop through the appropriate service
   or interactive-user channel and verify each required process remains alive
   before installation succeeds.
5. Extend installer contract tests, run formatting/build/tests, update the
   task queue, refresh graphify, and record the completed checkpoint.

## Security, UX, and recovery implications

- Users receive only read/execute access; installed files remain non-writable.
- Interactive components must not inherit the installer's elevated token.
- A startup failure remains an installation failure and invokes the existing
  rollback path instead of displaying a false success state.
- Rollback must restore the previous service and ProcessAgent running state;
  Desktop restoration is limited to a successful completed installation.

