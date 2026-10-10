# FG-142 checkpoint — strict cycle persistence and enforcement

## Summary

Implemented integrity-bound cycle policies, asymmetric break authorization,
effective-rule evaluation for process and browser consumers, and restart-safe
strict plan recovery.

## Files changed

- Domain policy, plan integrity, and effective-rule logic.
- Service distribution, strict persistence, and recovery reconciliation.
- Process monitor and browser extension enforcement.
- Cycle contracts and prepare-session break-rule validation.
- FG-142 tests, ADR, specification, threat model, and task log.

## Tests added

- Cycle policy effective-rule and tamper tests.
- Strict snapshot plan and break-exception round-trip test.
- Browser break exception transition test.

## Commands executed

- `dotnet format` on modified project files.
- `dotnet build --no-restore`.
- `dotnet test --no-build --no-restore`.
- `npm test` and `npm run check` in `extension`.
- `graphify update .`.

## Results

All current .NET tests pass (299), the extension suite passes (15), and the
extension type check passes. Final targeted format verification is recorded
after the checkpoint is closed.

## UX implications

Break permissions are temporary and cannot silently extend a session. A
consumer that cannot recover relaxation authority keeps focus enforcement.

## Security implications

Changing any cycle boundary, exception, or authorized phase invalidates the
policy integrity value. Corrupt strict state follows the existing fail-open
recovery path.

## Limitations

The dedicated reboot drill matrix, MV3 break alarm, history, and final
installer/E2E validation remain in FG-145 and later checkpoints.

## Next task

FG-143 — Desktop and tray cycle configuration and phase-status UX.
