# FG-123 Profile Duplication

## 1. Summary

Added persistent profile duplication to the Profiles screen. A duplicate copies
the source profile's description, accent, application rules, and website rules,
then becomes the selected profile so it can be edited immediately.

Every duplicate receives a new profile ID and every copied rule receives a new
rule ID. This avoids SQLite primary-key collisions and keeps later edits
independent from the source.

## 2. Files changed

- `TASKS.md`
- `docs/progress/20260730-0035-fg-123-profile-duplication-plan.md`
- `docs/progress/20260730-0055-fg-123-profile-duplication.md`
- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `src/FocusGuard.Desktop/MainWindow.axaml`
- `src/FocusGuard.Desktop/Resources/UiStrings.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.resx`
- `src/FocusGuard.Desktop/Resources/UiStrings.es.resx`
- `tests/FocusGuard.Desktop.Tests/FocusViewModelTests.cs`
- `tests/FocusGuard.Desktop.Tests/LocalizationTests.cs`
- Refreshed Graphify reports and graph exports

## 3. Tests added

- Duplicate persists and becomes selected.
- Copied application and website rules have independent IDs.
- Repeated copies receive collision-free names.
- Names remain within the 80-character storage limit.
- Active sessions disable and refuse duplication.
- English and Spanish duplicate-profile copy is available.

## 4. Commands executed

- Targeted tests before implementation to demonstrate the missing behavior
- `dotnet format FocusGuard.slnx`
- `dotnet format FocusGuard.slnx --verify-no-changes`
- Targeted duplication and localization tests
- `./scripts/build.ps1`
- `./scripts/test.ps1`
- `graphify update .`
- Git diff and whitespace audits

## 5. Results

- Initial targeted test: failed because duplication did not exist, as expected
- Targeted tests after implementation: 11 passed
- Format verification: passed
- Release build: passed with 0 warnings and 0 errors
- .NET tests: 237 passed, 0 failed, 0 skipped
- Browser-extension tests: 14 passed, 0 failed
- Graphify: 354 files, 2,660 nodes, 4,521 edges, 387 communities

## 6. UX implications

The Profiles screen now shows a localized `Duplicate profile` action beside the
profile selector. On success, the copy is selected and a message explains that
it is ready to edit and affects future sessions only.

Repeated copies use names such as `Copy of Deep Work (2)` or their localized
Spanish equivalent. The action is unavailable during active sessions.

## 7. Security implications

Duplication cannot mutate or replace an active immutable policy. It is rejected
while a session is active or another view-model operation is busy. No browser,
service, enforcement, or recovery behavior changed.

## 8. Limitations

- Rename and delete are not included; they require their own validation and
  destructive-confirmation UX.
- The feature was verified through compiled XAML and automated view-model,
  persistence-contract, localization, and full regression tests. No screenshot
  review was performed in this checkpoint.

## 9. Next task

Add profile rename with inline validation and active-session locking. Keep
profile deletion as a later destructive checkpoint with explicit confirmation.
