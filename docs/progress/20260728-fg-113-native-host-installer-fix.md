# FG-113 native-host installer fix checkpoint

## Summary

Fixed the installed-extension health failure. The distribution now packages
`focusguard.native.json`, and the installer places it beside
`FocusGuard.NativeMessaging.exe`, matching the host's existing validation
contract. The same fixed extension origin remains in the browser registration
and local validation manifests.

## Files changed

- `scripts/build-installer.ps1`
- `scripts/install-focusguard.ps1`
- `scripts/test-installer.ps1`
- `TASKS.md`
- this plan and checkpoint under `docs/progress/`

## Tests added

Installer contract assertions require both packaging and final placement of the
local native-host manifest.

## Commands executed

- `scripts/test-installer.ps1`
- `dotnet test tests/FocusGuard.NativeMessaging.Tests/... -c Release --no-restore`
- `scripts/build-installer.ps1 -OutputDirectory artifacts/installer/FocusGuard-0.1.0-native-host-fix`
- owner-authorized copy of the reviewed installed Chrome manifest to
  `C:\Program Files\FocusGuard\focusguard.native.json`
- installed process and authoritative status checks

## Results

- installer tests passed
- native messaging tests passed: 23/23
- self-contained graphical installer package built successfully
- Chrome launched the installed native host
- authoritative health changed from `extensionReady=false` to
  `extensionReady=true`
- installed ProcessAgent is running

## UX implications

Already-loaded Chrome extensions reconnect without reinstalling FocusGuard or
restarting Windows. The current session remains immutable; a session originally
started without enforcement must be stopped and replaced by an explicitly
opted-in session.

## Security implications

No wildcard origins, enterprise browser policies, or new privileges were added.
Missing/invalid manifests still fail open. The host validates only the fixed
development extension ID before accessing service IPC.

## Limitations

Chrome/Edge store signing is still required for frictionless extension
installation on unmanaged Windows. The fix repairs native messaging after the
extension is present; it does not bypass browser installation controls.

## Next task

Stop the current non-enforcing session, enable the enforcement switch, start a
new finite session, and verify both a configured website and a safe test
executable. Complete FG-113 after owner store publication and signed-package
acceptance.
