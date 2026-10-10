# FG-150 enforcement health and installer checkpoint

## 1. Summary

Corrected the active-session health presentation so enabled but unconfirmed
enforcement is shown as degraded, never as intentionally disabled `Timer only`.
Added the missing property notifications for the first authoritative active status.
Aligned the .NET and browser policy-integrity contracts for cycle-count policies,
and added fixed cross-runtime hash vectors for both simple-timer and cycle modes.

Also bounded strict-session persistence reconciliation so concurrent Desktop,
ProcessAgent, and extension status traffic does not repeat protected-state reads,
and made setup reject a service that is merely running but cannot answer repeated
authoritative IPC probes. Regenerated the standalone 0.1.1 Windows installer.

## 2. Files changed

- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml`
- `src/FocusGuard.Desktop/Resources/UiStrings.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.resx`
- `src/FocusGuard.Desktop/Resources/UiStrings.es.resx`
- `src/FocusGuard.Desktop/Program.cs`
- `src/FocusGuard.Desktop/ServiceHealthCheck.cs`
- `src/FocusGuard.Domain/PolicyDistribution.cs`
- `src/FocusGuard.Service/InMemoryFocusService.cs`
- `src/FocusGuard.DrillClient/Program.cs`
- `extension/manifest.json`
- `extension/test/policy-integrity.test.ts`
- `scripts/install-focusguard.ps1`
- `scripts/test-installer.ps1`
- corresponding Desktop, Domain, and Service test files
- `TASKS.md`

## 3. Tests added

- Active enforcement indicator states: protected, degraded, and intentional
  timer-only.
- First connected active status raises every enforcement presentation property.
- Concurrent status calls recover persisted strict state once.
- Active strict sessions reconcile persistence at a bounded interval.
- Headless Desktop authoritative service health probes.
- Fixed simple-timer and cycle-count SHA-256 vectors shared by .NET and JavaScript.

## 4. Commands executed

- Production service/process/pipe and persisted-profile read-only diagnostics.
- `dotnet format FocusGuard.slnx --no-restore`
- `dotnet build FocusGuard.slnx --configuration Release --no-restore`
- `dotnet test FocusGuard.slnx --configuration Release --no-build --no-restore`
- `npm test` and `npm run check` in `extension/`
- `scripts/test-installer.ps1`
- `scripts/build-installer.ps1 -Version 0.1.1`
- standalone `--package-self-test` and `--smoke-test`

## 5. Results

- Release build: zero warnings and zero errors.
- .NET: 329 tests passed.
- Extension: 16 tests passed; TypeScript check passed.
- Installer contracts, embedded-payload self-test, and graphical smoke test passed.
- Artifact: `artifacts/installer/FocusGuard-0.1.1-Setup.exe`.
- Size: 349,788,344 bytes.
- SHA-256:
  `75A8EA612363F58F39FDE471C7DBB006C6950300806DCD3186E45C8C0880C3B0`.
- Packaged Desktop, Service, and Domain DLL hashes differ from the currently
  installed 0.1.0 payload, proving this is not a renamed copy of the old setup.

## 6. UX implications

An active session now says `Protected` only after the service confirms all requested
channels. Missing acknowledgements show `Degraded`; `Timer only` is reserved for
the explicit blocking-off setting. The installer restarts Desktop and its supervised
ProcessAgent before showing success.

## 7. Security implications

The service remains authoritative. Neither process existence nor a running timer
is treated as proof of enforcement. Both browser and application channels continue
to require the exact immutable policy ID and SHA-256 acknowledgement; the browser
hash fix restores that contract rather than bypassing it. Strict recovery,
expiration, and rollback behavior remain finite and unchanged.

## 8. Limitations

The installed machine still contains the older 0.1.0 binaries until the owner runs
the new elevated installer. The expired user session could be inspected read-only,
but was not restarted automatically because that would create a new focus session
and could close Firefox or Claude without explicit authorization. Store-signed
browser distribution remains separate from this checkpoint.

## 9. Next task

Run `FocusGuard-0.1.1-Setup.exe`, then start a short session. The active card must
show `Protected` or the truthful `Degraded` state; it must not show `Timer only`
while blocking is enabled.
