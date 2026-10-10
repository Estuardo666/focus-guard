# FG-156 — Cycle phase notifications above the taskbar clock

## Summary

Added a calm, non-activating corner notification for service-authoritative
focus and short-break boundaries. Adjacent phases are combined in one card: a
focus-to-short-break transition says that focus completed and the short break
started, while the inverse says the break completed and the next focus cycle
started. Countdown polls are deduplicated, simple timers are ignored, and long
breaks remain silent except that the focus phase ending before one is still
announced.

The existing Notifications settings placeholder is now a persistent,
enabled-by-default `Focus and short-break notifications` preference. Existing
settings files without the new field retain the enabled default.

## Files changed

- `src/FocusGuard.Desktop/CyclePhaseNotifications.cs`
- `src/FocusGuard.Desktop/DesktopSettings.cs`
- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `src/FocusGuard.Desktop/MainWindow.axaml`
- `src/FocusGuard.Desktop/MainWindow.axaml.cs`
- `src/FocusGuard.Desktop/SessionToastWindow.axaml`
- `src/FocusGuard.Desktop/SessionToastWindow.axaml.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.resx`
- `src/FocusGuard.Desktop/Resources/UiStrings.es.resx`
- `tests/FocusGuard.Desktop.Tests/CyclePhaseNotificationTests.cs`
- `tests/FocusGuard.Desktop.Tests/LocalizationTests.cs`
- `extension/manifest.json`
- `TASKS.md`
- `graphify-out/GRAPH_REPORT.md`, `graphify-out/graph.json`, and
  `graphify-out/graph.html`
- `docs/progress/20260804-2248-fg-156-cycle-phase-notifications-plan.md`
- `docs/progress/20260804-2358-fg-156-cycle-phase-notifications.md`

## Tests added

- Initial focus notification and countdown-poll deduplication.
- Focus/short-break bidirectional transition composition.
- Final focus/short-break completion deduplication.
- Long-break tracking without long-break notices.
- Simple-timer exclusion.
- Spanish transition copy.
- Preference default, persistence, and migration from an older settings file.
- Non-activating, taskbar-free, polite live-region XAML contract.
- Spanish catalog assertions and full English/Spanish resource-key parity.

## Commands executed

- `dotnet test tests/FocusGuard.Desktop.Tests/FocusGuard.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~CyclePhaseNotificationTests|FullyQualifiedName~LocalizationTests"`
- focused `dotnet format` and `--verify-no-changes` for all touched C# files
- `dotnet build FocusGuard.slnx -c Release --no-restore`
- `dotnet test tests/FocusGuard.Desktop.Tests/FocusGuard.Desktop.Tests.csproj -c Release --no-build`
- `dotnet test FocusGuard.slnx -c Release --no-build`
- `graphify update .`
- `npm run check` and `npm test` under `extension/`
- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.5`
- `scripts/test-installer.ps1`
- `FocusGuard-0.1.5-Setup.exe --package-self-test`
- SHA-256 checks for the standalone EXE and ZIP.

## Results

- Focused notification/localization tests: 16/16 passed.
- Desktop tests: 144/144 passed.
- Full .NET suite: 351/351 passed.
- Extension type check passed; extension tests: 16/16 passed.
- Release build: succeeded with 0 warnings and 0 errors.
- Touched C# formatting verification: passed.
- Repository-wide formatting verification still reports pre-existing LF/CRLF
  differences in unrelated owner changes such as `CatppuccinAccents.cs` and
  `DesignSystemTests.cs`; this checkpoint did not rewrite those files.
- The first 0.1.5 packaging attempt stopped because the extension manifest was
  still 0.1.4. The manifest was aligned to 0.1.5 and the complete rebuild passed.
- Installer contract tests and standalone embedded-package self-test passed.
- Standalone setup: `artifacts/installer/FocusGuard-0.1.5-Setup.exe`,
  349,792,440 bytes, SHA-256
  `1ACA7D2108A4B0CD4E52286BEA52E6D3B4D5497387BC9E46D374797906B71B48`.
- Bundle ZIP: `artifacts/installer/FocusGuard-0.1.5-win-x64.zip`,
  513,387,188 bytes, SHA-256
  `58FB119DAABCD8F7B8F8506BA548E4CA1A08DC9CD1B646409A6EB34A48036197`.

## UX implications

The notification appears above the clock without entering the taskbar or
stealing keyboard focus, dismisses after nine seconds, and uses a polite screen
reader live region. One card describes both sides of an adjacent transition,
avoiding duplicate bursts. Users can turn the feature off in Settings.

## Security implications

The new tracker does not use a local timer and cannot authorize or skip a break.
It only reacts to validated phase data returned by the authoritative service.
Service loss does not fabricate a completion notice, and enforcement/recovery
logic is unchanged.

## Limitations

- Long-break start/end notices are intentionally excluded because the requested
  scope named focus and short breaks. A focus ending into a long break is still
  announced as a focus completion.
- No privileged install or real timed session was started in this checkpoint;
  the standalone payload was built and self-tested without changing Windows.

## Next task

Continue the remaining owner-present installed-UI validation in FG-153/FG-155,
then complete the reboot and controlled-recovery drills tracked by FG-145.
