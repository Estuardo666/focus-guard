# FG-112 real enforcement diagnostics checkpoint

## Summary

Repeated the owner-present production drill with the installed Windows service.
Fixed a production harness endpoint mismatch and a deterministic strict-snapshot
replacement failure. The service now remains running throughout a strict session,
releases enforcement immediately when browser health expires, and removes the
persisted snapshot at expiration.

Real process termination is still not demonstrated. The installed service runs as
`LocalService`; both `Process.MainModule` and `PROCESS_QUERY_LIMITED_INFORMATION`
cannot inspect a process owned by the interactive user in this environment. The
process worker therefore cannot evaluate or terminate that cross-session target.
Changing the service to `LocalSystem` would contradict the existing installer and
security checkpoints. The reviewable alternative is a separately specified,
service-authorized per-user enforcement agent.

## Files changed

- `src/FocusGuard.DrillClient/Program.cs`
- `src/FocusGuard.Infrastructure.Windows/Processes/WindowsProcessInspector.cs`
- `src/FocusGuard.Service/FocusControlPipeService.cs`
- `src/FocusGuard.Service/FocusSessionExpirationWorker.cs`
- `src/FocusGuard.Service/ProcessEnforcementWorker.cs`
- `src/FocusGuard.Service/ServiceFailureLog.cs`
- `src/FocusGuard.Service/StrictPolicyPersistence.cs`
- `tests/FocusGuard.Infrastructure.Windows.Tests/WindowsProcessInspectorTests.cs`
- `tests/FocusGuard.Service.Tests/StrictPolicyPersistenceTests.cs`

## Tests added

- Concurrent strict snapshot reads and atomic replacements.
- Retry of a transient Windows replacement lock.
- Limited-information inspection of the current Windows process.

## Commands executed

- `dotnet build FocusGuard.slnx -c Release --no-restore`
- `dotnet test tests/FocusGuard.Service.Tests/FocusGuard.Service.Tests.csproj -c Release --no-restore`
- `dotnet test tests/FocusGuard.Infrastructure.Windows.Tests/FocusGuard.Infrastructure.Windows.Tests.csproj -c Release --no-restore`
- `dotnet format FocusGuard.slnx --no-restore`
- `scripts/build-installer.ps1`
- `scripts/install-focusguard.ps1`
- production `FocusGuard.DrillClient` activation, acknowledgement, status, and activity commands
- real medium-integrity target launches through Explorer

## Results

- Solution build: passed with zero warnings.
- Service tests: 45 passed.
- Windows infrastructure tests: 14 passed.
- Installed service: `Running`, `Automatic`, `LocalService`.
- Strict persistence: survived repeated reconciliation beyond the prior failure
  point; expired snapshot was removed.
- Browser heartbeat loss: `extensionReady=false` and
  `enforcementConnected=false` immediately after lease expiry.
- Process target: remained running; no false audit event was written.

## UX implications

The Desktop can report degraded enforcement honestly rather than claiming a
blocked process. Expiration remains automatic without Desktop involvement.

## Security implications

No account, privilege, or ACL was widened. Emergency diagnostics persist only UTC
time, worker name, exception type, and HRESULT. The failed process drill confirms
that `LocalService` is least-privileged but cannot implement cross-user process
termination directly.

## Limitations

- Chrome and Edge unpacked-extension activation is not yet proven.
- Real OS reboot with an active policy is not yet run for FG-112.
- Cross-session process enforcement needs an explicit architecture decision.

## Next task

Complete component-loss/browser validation and CI, then specify the smallest
per-user process-enforcement agent vertical slice for owner review.
