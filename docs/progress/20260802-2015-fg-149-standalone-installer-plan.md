# FG-149 standalone installer and enforcement recovery plan

## Root cause

The graphical `FocusGuard.Setup.exe` is self-contained only for its own UI. It
still resolves `install-focusguard.ps1` and all product binaries beside the EXE.
That allowed a new-looking setup executable to reinstall a stale adjacent
Desktop payload. The installed `FocusGuard.Desktop.dll` hash differs from the
current package, and the persisted settings therefore still omit the blocking
opt-in and show `Timer only`. The install script also starts ProcessAgent before
Desktop; Desktop owns agent supervision and starts a second instance.

## Plan

1. Build a versioned payload archive before publishing Setup and embed it into
   the single-file executable.
2. Extract the embedded payload into a bounded temporary directory with path
   traversal protection, and clean it when Setup exits.
3. Start Desktop from the installer and let Desktop own ProcessAgent startup;
   verify the service, Desktop, exactly one ProcessAgent, and installed payload
   hashes before reporting success.
4. Add installer contract and standalone-payload regression checks.
5. Run formatting, extension tests, .NET build/tests, installer contract/smoke
   checks, build the Release installer, and verify the final EXE in isolation.

## Security, UX, and recovery implications

- Security: archive extraction accepts only relative paths beneath a unique
  temporary root; installer hash verification prevents false success on stale
  or partially copied binaries.
- UX: one EXE is sufficient; completion means the updated Desktop and its single
  supervised agent are actually running.
- Recovery: existing service/data backup and rollback remain authoritative. The
  extracted payload is temporary and does not alter recovery semantics.
