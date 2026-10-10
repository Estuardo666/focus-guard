# Installer update — current cycle-capable build

## Summary

Regenerated the standard Windows `win-x64` installer bundle from the current
repository sources. The package includes the Service, Recovery, Desktop,
ProcessAgent, NativeMessaging host, graphical Setup, install/uninstall scripts,
Chrome/Edge native manifests, and browser-extension archives.

## Outputs

- [Installer package](C:/Users/Stuart/Documents/FocusGuard/FocusGuard-Autopilot/artifacts/installer/FocusGuard-0.1.0)
- [ZIP bundle](C:/Users/Stuart/Documents/FocusGuard/FocusGuard-Autopilot/artifacts/installer/FocusGuard-0.1.0-win-x64.zip)
- Package size: 673,363,925 bytes.
- ZIP size: 280,796,755 bytes.
- Setup SHA-256: `CAC278A21D7D7D98129A812C859401B3A7B08D6C11E1B2753E503FB45550ECB5`.
- ZIP SHA-256: `0A3F0695D9F9368AB70B874EE4134A0F771054247CFC4706A129A3F5A46811DB`.

## Commands executed

- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.0`
- `scripts/test-installer.ps1`
- `dotnet src/FocusGuard.Setup/bin/Release/net10.0-windows/FocusGuard.Setup.dll --smoke-test`
- `scripts/test-browser-extension-package.ps1`
- Manifest and ZIP content verification.

## Results

- Release publishing completed successfully.
- Installer script contracts passed.
- Setup smoke test passed without elevation.
- Chrome and Edge extension package tests passed.
- All 14 manifest entries exist in the generated package.
- ZIP contains Setup and the manifest.

## UX, security, and recovery implications

The generated artifacts contain the latest Desktop cycle configuration and
prepared-plan review changes. No install, service registration, registry, ACL,
browser-policy, or uninstall operation was executed. Elevated installation,
real recovery, reboot, and rollback drills remain owner-present validation.

## Next task

Owner-present installation and recovery validation for FG-145, followed by the
statistics/history implementation once the Pomodoro history prerequisite is
complete.
