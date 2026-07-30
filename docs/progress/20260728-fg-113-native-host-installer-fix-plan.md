# FG-113 native-host installer fix plan

## Problem

The installed MV3 extension has the expected fixed ID and both browser registry
entries point to valid manifests. However, `FocusGuard.NativeMessaging.exe`
validates the launch origin against `focusguard.native.json` beside the
executable, while the installer only copied browser registration manifests into
the `native-messaging` subdirectory. The host therefore exits before IPC and the
Desktop correctly reports the browser extension as unavailable.

## Plan

1. Generate and package the local validation manifest from the same reviewed
   manifest object used for Chrome and Edge.
2. Require it during installation and copy it beside the native-host executable.
3. Add installer contract tests that fail if either packaging or placement is
   removed.
4. Run installer tests, native-host tests, build, and the installed native-health
   drill; then apply the minimal owner-authorized machine hotfix.

## Implications

- **Security:** the host continues to allow only the fixed extension origin; no
  wildcard or browser policy is introduced.
- **UX:** an installed/enabled extension can become healthy without reinstalling
  Windows or manually restarting the service.
- **Recovery:** installer tree backup/rollback includes the new file, and absent
  or invalid manifests remain fail-open.
