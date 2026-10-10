# Installer update — Pomodoro cycle build

## Summary

Regenerated the self-contained Windows installer bundle from the current
Pomodoro-capable Service, Desktop, ProcessAgent, NativeMessaging, Recovery,
Setup, and browser-extension sources.

## Output

- `artifacts/installer/FocusGuard-0.1.0-pomodoro/`
- `artifacts/installer/FocusGuard-0.1.0-win-x64-menu.zip`

The package manifest contains the five runtime executables, graphical setup,
install/uninstall scripts, Chrome and Edge native-host manifests, and both
browser-extension archives.

## Validation

- `scripts/test-installer.ps1` passed.
- Release `win-x64` publishing completed without errors.
- No installation, service registration, registry mutation, or uninstall was
  performed on the machine.

## Limitation

This checkpoint validates packaging and script contracts. Full install,
upgrade, uninstall, reboot drill, and production canary validation remain in
FG-145.
