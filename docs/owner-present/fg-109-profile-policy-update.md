# FG-109 owner-present production update

## Authorized scope

Update the existing `FocusGuard` production installation from the reviewed
FG-109 bundle. The owner stated that the updated application is open and that
the current Codex process has administrator privileges.

## Preconditions

- Formatting, Release build, .NET tests, extension tests, and installer contract
  tests pass.
- `FocusGuard` points only to `%ProgramFiles%\FocusGuard\FocusGuard.Service.exe`.
- No strict policy snapshot is active or pending.
- The existing installer creates a temporary backup before replacing binaries
  and restores binaries, ACLs, and service state on failure.

## Mutation

1. Build the production bundle with `scripts/build-installer.ps1`.
2. Close only the running `FocusGuard.Desktop` process so its binaries can be
   serviced.
3. Run `scripts/install-focusguard.ps1` against the reviewed bundle.
4. Verify SCM identity/state, installed hashes, inactive status IPC, and absence
   of strict state.
5. Launch the installed Desktop and inspect its accessible UI.

## Rollback

The installer automatically restores the temporary pre-update binary backup,
installation ACL, machine-state ACL, and prior running service state if any
copy, ACL, SCM, or startup step fails. No profile database is removed or
migrated destructively. If post-install verification fails after a successful
installer return, stop and preserve diagnostics; do not activate a session.
