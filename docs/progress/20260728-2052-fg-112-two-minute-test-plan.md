# FG-112 two-minute test preset plan

1. Add a finite two-minute test preset without changing the 50-minute default.
2. Cover the preset and its exact duration with a Desktop view-model test.
3. Run Desktop tests and build, then update the installed Desktop application.

Security and recovery: the preset remains above the one-minute protocol minimum,
uses the same service-authoritative expiration, and releases automatically.

UX: label the short duration explicitly as a test option so it is not confused
with a normal focus preset.

## Checkpoint result

### Summary

Added `2 minutes (test)` while preserving 50 minutes as the default.

### Files changed

- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `tests/FocusGuard.Desktop.Tests/FocusViewModelTests.cs`
- this checkpoint

### Tests added

- `DurationPresetsIncludeFiniteTwoMinuteTestOption`

### Commands and results

- Desktop tests: 20/20 passed.
- Desktop Release build: passed with zero warnings and zero errors.
- Installer package rebuilt and installed after stopping the per-user agent that
  held `clrjit.dll`; service, agent, and Desktop restarted successfully.
- Installed and packaged Desktop DLL SHA-256 values match.

### UX implications

Users can select a clearly marked two-minute test without changing the normal
50-minute default.

### Security and recovery implications

No enforcement semantics changed. The service remains authoritative and the
two-minute session expires through the existing automatic release path.

### Limitations

The first install attempt reproduced the open-runtime-file rollback defect when
the per-user agent held `clrjit.dll`. Manual agent shutdown allowed the verified
install; the installer must be updated to close the agent itself.

### Next task

Use the installed two-minute preset for an owner-present expiration test and
then fix the installer process-agent shutdown/rollback gap.
