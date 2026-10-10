# FG-148 — Enforcement opt-in persistence and active health detail

## 1. Summary

Fixed the post-reinstall `Timer only` regression. Desktop now persists the
user's explicit “Real app and site blocking” choice, restores it before another
session is started, and shows the full service-authoritative Connected,
Degraded, or enforcement-off explanation in the active workspace.

## 2. Root cause

`IsEnforcementEnabled` existed only in memory and defaulted to `false` whenever
Desktop started. Reinstall intentionally closes and restarts Desktop, so it
discarded the user's opt-in. The next activation request contained
`EnforcementEnabled=false`; the service correctly ran a timer without
distributing application or website policy. The compact active workspace then
showed only `Timer only`, while the existing detailed health message was no
longer visible on that screen.

Read-only runtime diagnostics confirmed the service was Running, Desktop,
ProcessAgent, and NativeMessaging were alive, all three named pipes existed,
the browser extension heartbeat was ready, and the expired reported session
had `EnforcementConnected=false`. The user's settings JSON contained no
enforcement preference, confirming the restart-state loss.

## 3. Files changed

- `src/FocusGuard.Desktop/DesktopSettings.cs`
- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml`
- `tests/FocusGuard.Desktop.Tests/FocusViewModelTests.cs`
- `TASKS.md`
- `docs/progress/20260802-1845-enforcement-opt-in-persistence-plan.md`
- this checkpoint
- regenerated installer directory and Setup executable
- refreshed `graphify-out/`

## 4. Tests added and corrected

- explicit enforcement opt-in survives a simulated Desktop restart and reaches
  the next activation request;
- changing the toggle persists to a fresh settings-store instance;
- active status distinguishes enforcement off, Degraded, and Connected;
- enforcement tests now use isolated temporary settings and never write the
  owner's real `%LOCALAPPDATA%\FocusGuard\desktop-settings.json`.

## 5. Commands executed

- read-only Windows service/process/pipe/ACL/registry and IPC diagnostics;
- targeted Desktop test project;
- scoped whitespace formatting and repository format verification;
- Release solution build;
- full solution tests;
- Release installer build;
- packaged installer contract test;
- graphical Setup smoke test;
- graphify update.

## 6. Results

- Desktop tests: 120 passed, 0 failed;
- full suite: 323 passed, 0 failed;
- Release build: 0 warnings, 0 errors;
- formatting: passed;
- installer contracts and graphical smoke test: passed;
- `FocusGuard.Setup.exe` SHA-256:
  `37785E1AD0CFEA7C129D766F9F2429AD5468E6412B879DC6686230C1BA5C463D`.

## 7. UX implications

After the owner enables real blocking once, ordinary Desktop restarts and
installer servicing preserve that choice. The active card again explains
whether enforcement is Connected, Degraded, or intentionally off instead of
showing only an ambiguous compact badge.

## 8. Security implications

The default remains disabled for missing or legacy settings, preserving the
explicit opt-in boundary. The persisted preference changes only the next
user-initiated activation request; it cannot activate a session by itself.
Service policy acknowledgement, finite expiration, and channel health remain
authoritative.

## 9. Recovery implications and limitations

No active policy or strict recovery state was changed. A unit-level restart
reproduction proves the original state-loss path is fixed. The regenerated EXE
still requires one owner-present reinstall and a controlled app/site session to
verify real enforcement on this machine.

## 10. Next task

Reinstall with the regenerated `FocusGuard.Setup.exe`, enable real blocking
once, start a short normal session, and verify one configured application plus
one configured site while the active status reads Connected.

