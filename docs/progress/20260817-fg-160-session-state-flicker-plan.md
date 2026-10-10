# FG-160 plan — stable authoritative session state

## Symptom

After starting either a simple focus timer or a Pomodoro cycle, the Desktop
alternates between the active and inactive workspaces while the service session
continues running.

## Root-cause hypothesis

`FocusViewModel.RefreshAsync` treats the optional activity-metrics request as if
it were the authoritative status request. Every fifth active refresh, a transient
failure from `GetActivityAsync` reaches the outer status error handler and sets
`IsActive` to false. The next successful status poll reads the still-active
service session and sets it back to true.

## Implementation plan

1. Add a regression test with an active authoritative status and a failing
   activity-metrics request.
2. Isolate best-effort activity refresh failures from authoritative session
   status updates while preserving cancellation semantics.
3. Run focused Desktop tests, formatting, build, and the full test suite.
4. Refresh graphify, update the task log, and regenerate and validate the
   versioned standalone installer.

## Security, UX, and recovery implications

- Security: the service remains the only authority for activation and expiry;
  cached activity counters grant no policy authority.
- UX: transient audit-pipe failures may leave blocked-attempt counts stale for
  one polling interval, but cannot replace the active timer with the setup UI.
- Recovery: a failure of the authoritative status request still reports the
  service as unreachable. Metrics retry automatically on a later poll.

## Acceptance criteria

- An activity request failure cannot change an active session to inactive.
- A status request failure retains the existing unreachable-service behavior.
- Simple timers and cycle sessions use the same stable refresh path.
- Formatting, build, tests, graph update, and installer validation pass.

## Checkpoint

### Summary

The Desktop now keeps the last authoritative active state when the optional
activity request times out or returns invalid data. Block counters remain at
their last known values and retry later instead of replacing the running timer
or cycle with the inactive setup screen.

### Files changed

- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `tests/FocusGuard.Desktop.Tests/FocusViewModelTests.cs`
- `extension/manifest.json`
- `TASKS.md`
- this checkpoint and generated graph files

### Tests added

- An active authoritative status remains active when its first supplemental
  activity-metrics request throws a timeout.

### Commands executed

- Focused regression before and after the fix.
- Targeted `dotnet format` verification for the changed C# files.
- `dotnet build FocusGuard.slnx -c Release --no-restore`.
- `dotnet test FocusGuard.slnx -c Release --no-build --no-restore`.
- `npm test` and `npm run check` under `extension`.
- `graphify update .`.
- `scripts/build-installer.ps1 -Version 0.1.10`.
- `scripts/test-installer.ps1`.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.10`.

### Results

- Regression: failed before the fix because `IsActive` became false; passed
  after the fix.
- Build: passed with zero warnings and zero errors.
- .NET: 360 passed, zero failed.
- Extension: 18 passed, zero failed; TypeScript check passed.
- Graph: refreshed to 3,610 nodes and 6,566 edges.
- Standalone installer `FocusGuard-0.1.10-Setup.exe` and complete ZIP were
  regenerated; installer contracts, embedded payload, extension archives, and
  hashes passed validation.

### UX implications

The Focus screen no longer alternates between active and inactive when audit
metrics briefly fail. Blocked-attempt counts can remain stale until a later
poll, which is calmer and truthful than hiding a session that is still active.

### Security implications

Only `GetStatusAsync` controls session state. Supplemental audit data cannot
activate, stop, extend, or release enforcement.

### Limitations

The repository-wide format check still reports pre-existing LF line endings in
unrelated dirty and untracked files. The changed C# files pass targeted format
verification. The new package was not installed because that changes the
machine and requires explicit owner authorization.

### Next task

Install `0.1.10` with the owner present and run one short timer plus one short
cycle while observing at least two activity polling intervals.
