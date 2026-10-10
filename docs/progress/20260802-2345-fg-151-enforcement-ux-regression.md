# FG-151 enforcement UX regression checkpoint

## 1. Summary

Fixed the UI/UX 2.0 workspace scroll regression and made active enforcement
presentation truthful for profiles without blocking rules. Timer/ring updates no
longer send the workspace to the top. An enabled session whose selected profile
contains no application or website rules now shows `No rules` with an actionable,
fully wrapped explanation instead of `Protected` or `Degraded`.

Read-only installed-machine diagnostics established that the sessions reported in
the screenshots used profile `2`, which currently has zero application and zero
website rules. The installed service, ProcessAgent, NativeMessaging host, and Chrome
extension 0.1.1 were present; Chrome loaded the current packaged integrity script.

## 2. Files changed

- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml.cs`
- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml`
- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.resx`
- `src/FocusGuard.Desktop/Resources/UiStrings.es.resx`
- `tests/FocusGuard.Desktop.Tests/DesktopLifecycleTests.cs`
- `tests/FocusGuard.Desktop.Tests/FocusViewModelTests.cs`
- `TASKS.md`

## 3. Tests added

- Workspace scroll resets only for active-state and Timer/Cycles workspace
  transitions, not ring text or reduced-motion updates.
- An active enforcement-enabled profile without rules is neither protected,
  degraded, nor timer-only and exposes the actionable no-rules explanation.

## 4. Commands executed

- Read-only production status, process, service, registry, extension, profile, and
  sanitized activity diagnostics.
- `dotnet format FocusGuard.slnx --no-restore`
- `dotnet format FocusGuard.slnx --no-restore --verify-no-changes`
- `dotnet build FocusGuard.slnx --configuration Release --no-restore`
- `dotnet test FocusGuard.slnx --configuration Release --no-build --no-restore`
- `graphify update .`

## 5. Results

- Format gate passed.
- Release build passed with zero warnings and zero errors.
- All 335 .NET tests passed.
- Graphify refreshed 492 code files: 3336 nodes, 5868 edges, 448 communities.

## 6. UX implications

The user keeps their scroll position while the timer changes or a duration control
updates the ring. The active blocking card wraps its full explanation and provides
the same text as a tooltip. Empty profiles clearly instruct the user to stop and
add rules.

## 7. Security implications

No service authority, acknowledgement, policy-integrity, lease, expiration, or
fail-open behavior changed. `Protected` now has an extra presentation guard: the
selected profile must actually contain a requested blocking channel.

## 8. Limitations

This checkpoint does not install or restart the newly built Desktop. The currently
installed app continues to run the preceding package until a new installer is built
and the owner performs the approved install. No live focus session was created as
part of diagnosis, avoiding unintended application or website interruption.

## 9. Next task

Regenerate the standalone installer, install it owner-present, and manually verify
scroll retention plus `No rules`, `Degraded`, and `Protected` states using the
appropriate profiles.
