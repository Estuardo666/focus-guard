# FG-159 plan — single-click reinstall with native-host quiescence

## Symptom

A reinstall closes FocusGuard processes, then fails on `clrjit.dll`; rollback can
fail on the same locked installation. Pressing **Instalar** a second time succeeds.

## Root cause

The MV3 extension reconnects `FocusGuard.NativeMessaging` one second after a
disconnect. The installer leaves the Chrome and Edge native-host registrations
active while it stops processes and copies files, so Chromium can relaunch the
host between the process sweep and `Copy-Item`. The rollback path stops only the
agent and service, leaving a relaunched native host or Desktop able to keep the
installation locked.

## Implementation plan

1. Save and temporarily remove the FocusGuard Chrome and Edge native-host
   registrations before stopping any installed runtime process.
2. Keep the registrations suspended through backup and binary replacement, then
   recreate them only after all installed files have been copied.
3. On failure, suspend the registrations again and stop every FocusGuard runtime
   process before removing or restoring the installation tree.
4. Add an installer regression contract that verifies both transaction orderings.
5. Run the installer contract, formatting/build/tests, refresh graphify, and
   regenerate and validate a versioned standalone installer.

## Security, UX, and recovery implications

- Security: only the two fixed FocusGuard native-host registry keys are suspended;
  their exact prior values are already captured and restored on rollback. Browser
  enforcement fails open while the host is unavailable.
- UX: one press of **Instalar** owns the complete update instead of surfacing a
  failure that requires a second press. The browser may briefly report FocusGuard
  unavailable during the bounded copy window.
- Recovery: rollback first prevents browser respawn, then stops Desktop, the
  ProcessAgent, and native messaging before restoring the verified backup and its
  previous registry values.

## Acceptance criteria

- Native-host registration is suspended before the initial runtime stop and copy.
- Rollback suspends registration and stops every FocusGuard runtime before touching
  the installation tree.
- Existing registry values are restored exactly after rollback.
- Installer contract, build, test, package self-test, and graph validation pass.

## Checkpoint

### Summary

The reinstall transaction now removes the fixed Chrome and Edge FocusGuard
native-host registrations before it closes installed runtimes. Chromium cannot
relaunch `FocusGuard.NativeMessaging` while binaries are copied. Desktop is
fully stopped before ProcessAgent enumeration, preventing its supervisor from
replacing the agent during the same window. Rollback repeats the registration
suspension and stops all installed runtimes before removing or restoring files.

### Files changed

- `scripts/install-focusguard.ps1`
- `scripts/test-installer.ps1`
- `extension/manifest.json` (0.1.9 package version)
- `TASKS.md`
- `docs/graphical-installer.md`
- this plan/checkpoint and generated graph files

### Tests added

The installer contract now requires native-host registration suspension and
verifies exact ordering in both the install and rollback transactions. The new
test failed against the prior script and passed after the fix.

### Commands executed

- `scripts/test-installer.ps1` before and after the implementation
- `dotnet format FocusGuard.slnx --no-restore --verify-no-changes`
- `dotnet build FocusGuard.slnx -c Release --no-restore`
- `dotnet test FocusGuard.slnx -c Release --no-build --no-restore`
- `npm test` and `npm run check` under `extension`
- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.9`
- `scripts/test-browser-extension-package.ps1 -Version 0.1.9`
- standalone `--package-self-test` and packaged graphical `--smoke-test`
- `graphify update .`

### Results

- Installer regression: passed; it failed before the source fix as expected.
- Release build: passed with 0 warnings and 0 errors.
- .NET tests: 359 passed, 0 failed.
- Extension tests: 18 passed, 0 failed; TypeScript check passed.
- Chrome/Edge packages, embedded payload self-test, and graphical smoke test:
  passed.
- Graph: refreshed to 3,608 nodes and 6,562 edges.
- Global format verification remains blocked by pre-existing LF endings in
  untracked/current Desktop and service test files plus existing whitespace in
  `MainWindow.axaml`; files in this change pass `git diff --check`.
- Packaged installer-script SHA-256 matches the source:
  `BA8004DD125528AA28B9A0FA880BE78D946E40728DD853C9462899E522C1A92F`.
- Standalone Setup SHA-256:
  `44EBA0470721EAEFF80A6389C147F4D582D97E5A375FA858CDBA96C5FF72B8B9`.

### UX implications

Setup retains its existing blocking-process disclosure. Once PowerShell owns the
transaction, it absorbs browser reconnects internally instead of returning a red
failure that requires a second Install press.

### Security implications

The suspension is limited to the two fixed FocusGuard native-host registry keys.
The service remains authoritative; browser blocking fails open while the host is
unregistered. Success writes the new known manifests, while rollback restores the
exact captured values.

### Recovery implications

The rollback path now removes both browser launch points, stops Desktop before
its supervised ProcessAgent, stops the native host, stops the service, restores
the fingerprint-verified backup, and finally restores prior registrations.

### Limitations

The real `C:\Program Files\FocusGuard` single-click reinstall was not executed
because it changes service, registry, process, ACL, and installed-file state and
requires explicit owner authorization. Therefore FG-159 remains in progress even
though source, regression, and standalone package validation pass.

### Next task

With the owner present, run `FocusGuard-0.1.9-Setup.exe` once while Chrome/Edge
and FocusGuard are active, confirm the operation succeeds without a second press,
and record installed service, Desktop, ProcessAgent, native-host, and rollback
health.
