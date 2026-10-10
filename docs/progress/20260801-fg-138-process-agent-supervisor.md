# FG-138 — continuously recover the installed ProcessAgent

## 1. Summary

Desktop now supervises its fixed sibling `FocusGuard.ProcessAgent.exe` for its
entire lifetime. It checks immediately and every five seconds, including while
the main window is hidden in the notification area. A failed check is contained
and retried instead of leaving application enforcement degraded until logon or
another reinstall.

## 2. Root cause

FG-131 repaired the agent only once, after a new primary Desktop instance
started. Its checkpoint explicitly excluded a continuous watchdog. If the agent
exited after that check while Desktop remained open or hidden, no component
attempted to relaunch it. The installer also launched the agent only once.

Read-only machine diagnostics separated this from a Windows Service failure:
the installed service was running as `LocalService`, the production control
pipe returned `serviceReady: true`, and the installed ProcessAgent was present.
The remaining design hole was loss after those one-shot startup checks.

## 3. Files changed

- `src/FocusGuard.Desktop/ProcessAgentStartup.cs`
- `src/FocusGuard.Desktop/App.axaml.cs`
- `tests/FocusGuard.Desktop.Tests/DesktopLifecycleTests.cs`
- `TASKS.md`
- `docs/progress/20260801-fg-138-process-agent-supervisor-plan.md`
- this checkpoint
- regenerated `graphify-out/`
- generated `artifacts/installer/FocusGuard-0.1.0-agent-supervisor/`

## 4. Tests added

- supervisor retries after its first agent check fails;
- discovery/access failures remain contained and do not escape into Desktop;
- existing tests continue to prove that a running exact sibling is not
  duplicated and a missing sibling is started.

## 5. Commands executed and results

- production status query: service ready, extension ready, finite prior session
  stopped, control pipe responsive;
- Windows service/process/registry/hash diagnostics: installed service running,
  ProcessAgent running, Run registrations correct, installed payload matches the
  previous `true-fade` package;
- targeted `dotnet format`: passed;
- Release build: passed with 0 warnings and 0 errors;
- Desktop lifecycle tests: 15/15 passed;
- full .NET suite: 278/278 passed;
- installer contract tests: passed;
- graphical setup smoke test: exit code 0;
- package generated at
  `artifacts/installer/FocusGuard-0.1.0-agent-supervisor/`;
- `git diff --check`: passed;
- `graphify update .`: passed.

## 6. UX implications

Users no longer need to reinstall, sign out, or manually relaunch an invisible
agent after it exits. Recovery occurs within five seconds while Desktop is
running. Existing degraded wording remains truthful during the recovery window.

## 7. Security implications

The supervisor launches only `FocusGuard.ProcessAgent.exe` beside the running
Desktop. It accepts no configurable path, arguments, remote input, elevation,
or executable payload. Desktop still does not enforce policy; the Service
remains authoritative for session state and finite expiration.

## 8. Recovery implications and limitations

Launch and discovery failures remain fail-open, visible as degraded health, and
are retried. Exiting Desktop intentionally also stops its supervisor; the
independent Run registration remains the logon recovery path.

The new package was built and smoke-tested but was not installed because the
current Codex process is not elevated. The installed Desktop DLL therefore still
has the previous hash and behavior until the owner runs the new setup.

## 9. Next task

Owner-present installation of the FG-138 package, followed by a real drill:
stop the ProcessAgent while Desktop remains in the notification area, confirm a
new PID appears within five seconds, and confirm an application-only session
returns from degraded to connected.
