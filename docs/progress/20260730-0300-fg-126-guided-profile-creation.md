# FG-126 — Guided profile creation

## 1. Summary

Added an onboarding-style profile creator inside the existing Profiles area. It asks for a profile name, offers transparent distraction suggestions, accepts multiple custom website or application rules, shows a live rule summary, and persists and selects the completed profile.

Suggestions begin unselected and always display the exact domains they add. Creating an empty profile remains valid. Cancelling discards the draft, and profile creation remains unavailable while a focus session is active.

## 2. Files changed

- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `src/FocusGuard.Desktop/MainWindow.axaml`
- `src/FocusGuard.Desktop/MainWindow.axaml.cs`
- `src/FocusGuard.Desktop/Styles/Controls.axaml`
- `src/FocusGuard.Desktop/Resources/UiStrings.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.resx`
- `src/FocusGuard.Desktop/Resources/UiStrings.es.resx`
- `tests/FocusGuard.Desktop.Tests/FocusViewModelTests.cs`
- `tests/FocusGuard.Desktop.Tests/DesignSystemTests.cs`
- `tests/FocusGuard.Desktop.Tests/LocalizationTests.cs`
- `TASKS.md`
- `graphify-out/`

## 3. Tests added

- Suggestion labels expose their exact domains and start unselected.
- Selected suggestions and custom rules persist into the new selected profile.
- An empty profile can be created.
- Invalid and duplicate custom rules are rejected.
- Cancelling and active-session guards prevent persistence.
- The guided creator uses accessible suggestion controls with visible domain summaries.
- New profile creation strings exist in English and Spanish.

## 4. Commands executed

- Targeted `dotnet test` for FG-126 desktop behavior and design contracts.
- `dotnet format FocusGuard.slnx --no-restore --verbosity minimal`
- `scripts/format.ps1`
- `scripts/build.ps1`
- `scripts/test.ps1`
- `graphify update .`

## 5. Results

- Targeted tests: 13 passed, 0 failed.
- Full .NET suite: 251 passed, 0 failed.
- Browser extension suite: 14 passed, 0 failed.
- Release build: succeeded with 0 warnings and 0 errors.
- Format verification: passed.
- Graphify code graph refreshed.

## 6. UX implications

- “New profile” now opens a focused, friendly creation flow without removing the normal application navigation.
- Suggested services are optional, visually selectable, and disclose the domains they will add.
- Users can add several domains or `.exe` application names before saving.
- A live summary communicates how many website and application rules will be created.
- The flow does not ask for session duration, administrator permission, service installation, or a browser extension because those concerns belong to session start and system setup, not profile definition.
- Controls and strings use the existing semantic design tokens and localization resources.

## 7. Security implications

- Existing domain and application-rule constructors remain the validation boundary.
- Query-bearing or malformed website inputs are rejected.
- Duplicate rules are not added.
- The change only configures profiles; it does not elevate privileges, install components, or alter enforcement.
- Active sessions retain the existing protection against profile mutation.

## 8. Limitations

- Automated behavior, compiled XAML, style contracts, and the full regression suite were verified.
- A local desktop instance was launched for visual inspection, but the configured desktop-control CLI was unavailable, so no automated screenshot walkthrough was completed. The launched process was stopped after validation.
- Suggestions currently cover the initial fixed set of popular services; installed desktop applications are not auto-detected.

## 9. Next task

Review the next unchecked item in `TASKS.md` and confirm its acceptance criteria before beginning another architectural checkpoint.
