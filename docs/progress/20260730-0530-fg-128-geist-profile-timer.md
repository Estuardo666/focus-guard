# FG-128 — Geist typography and profile-colored timer

## 1. Summary

Bundled Geist as the global Desktop UI family, increased the profile header
title by exactly 25% and made it Bold, changed the active timer arc and both
halo layers to the selected profile gradient, and increased the timer diameter
to create more space around its value and caption.

## 2. Files changed

- `assets/branding/fonts/Geist-Regular.ttf`
- `assets/branding/fonts/Geist-Medium.ttf`
- `assets/branding/fonts/Geist-SemiBold.ttf`
- `assets/branding/fonts/Geist-Bold.ttf`
- `assets/branding/fonts/OFL-Geist.txt`
- `src/FocusGuard.Desktop/FocusGuard.Desktop.csproj`
- `src/FocusGuard.Desktop/MainWindow.axaml`
- `src/FocusGuard.Desktop/Styles/Controls.axaml`
- `src/FocusGuard.Desktop/Styles/Tokens.axaml`
- `tests/FocusGuard.Desktop.Tests/DesignSystemTests.cs`
- `TASKS.md`
- `docs/progress/20260730-0500-fg-128-geist-profile-timer-plan.md`
- `docs/progress/20260730-0530-fg-128-geist-profile-timer.md`

## 3. Tests added

- Extended gradient coverage to require `ProfileAccentGradient` on the active
  arc and both glow layers.
- Added coverage for the packaged Geist family, Bold 31.25 px profile title,
  semantic 184 px timer diameter, and centered-copy spacing.

## 4. Commands executed

- Downloaded the four required Geist weights and OFL license from the upstream
  `vercel/geist-font` repository.
- `dotnet format FocusGuard.slnx --no-restore`
- `dotnet format FocusGuard.slnx --no-restore --verify-no-changes`
- `dotnet test tests/FocusGuard.Desktop.Tests/FocusGuard.Desktop.Tests.csproj -c Release --no-restore`
- `dotnet build FocusGuard.slnx -c Release --no-restore`
- `graphify update .`

## 5. Results

- Formatting verification passed.
- Desktop tests passed: 74 passed, 0 failed, 0 skipped.
- Release solution build passed with 0 warnings and 0 errors.
- The first new test run exposed an incorrect XML namespace in its token
  helper; the helper was corrected and the suite rerun successfully.

## 6. UX implications

All Desktop windows inherit packaged Geist while dedicated icon glyphs retain
their Segoe icon-family overrides. The larger Bold profile name improves header
hierarchy. The timer now visually belongs to its profile, and the larger ring
provides more breathing room without changing the time value or caption size.
Strict-mode and exact-expiration messaging remain unchanged.

## 7. Security implications

None. No session, enforcement, persistence, IPC, recovery, logging, or URL
behavior changed. Font assets are bundled and require no machine installation.

## 8. Limitations

This checkpoint validates XAML structure, resource packaging, tests, and build.
It does not include a new owner-approved screenshot baseline.

## 9. Next task

Continue with the next owner-requested visual or functional milestone.
