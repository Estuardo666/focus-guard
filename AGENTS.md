# AGENTS.md

## Mission

Build FocusGuard according to `SPEC.md`, `UX_SPEC.md`, `THREAT_MODEL.md`, `AUTOPILOT.md`, and the ADRs under `docs/adr`.

Written specifications are the source of truth. Do not silently broaden scope.

## Required workflow

For every task:

1. Read the relevant specification sections.
2. Inspect existing code and tests.
3. Write a concise implementation plan in the task log.
4. Identify security, UX, and recovery implications.
5. Implement the smallest complete vertical slice.
6. Add or update automated tests.
7. Run formatting, build, and relevant tests.
8. Update `TASKS.md`.
9. Record a checkpoint in `docs/progress/`.
10. Continue only when the current acceptance criteria pass.

## Autonomous execution rules

- Work through `TASKS.md` in order.
- Never skip a failed test.
- Make at most one architectural milestone per checkpoint.
- Commit-sized changes should remain reviewable.
- When blocked, document the blocker and move only to a task that does not depend on it.
- Do not replace real validation with mocks merely to make tests green.
- Do not claim a feature works unless an automated test or manual verification record supports it.
- Do not install services or change the machine without explicit user authorization.
- When the owner explicitly authorizes an owner-present milestone, Codex may execute the approved machine-changing commands directly if the current execution context already has the required privileges.
- Codex must read and report command failures itself, investigate them with read-only diagnostics, apply reviewable fixes, and perform the documented rollback verification.
- Never auto-elevate, automate UAC or credential dialogs, request credentials, or weaken accounts/ACLs merely to bypass a privilege or startup failure.
- Do not request or store secrets.

## Safety boundaries

Do not implement:

- process hiding
- rootkit behavior
- credential collection
- keylogging
- screen capture
- covert persistence
- antivirus disabling
- code or DLL injection
- bootloader changes
- destructive anti-uninstall behavior
- HTTPS interception
- kernel drivers
- blocking of recovery or security tools
- remote administration or surveillance
- arbitrary XAML or plugin code loading

Strict mode must be difficult to disable casually, not impossible for a determined administrator.

Every strict feature must:

- be explicitly enabled
- have a finite end time
- survive an ordinary reboot only after persistence tests exist
- release automatically
- have a documented recovery path
- log activation and recovery
- be covered by tests

## Code quality

- Enable nullable reference types.
- Treat warnings as errors.
- Prefer immutable records.
- Keep platform code behind interfaces.
- Pass `CancellationToken`.
- Inject time through `TimeProvider`.
- Use UTC internally and monotonic elapsed time for duration checks.
- Validate all IPC messages.
- Never deserialize arbitrary CLR object graphs.
- Use structured logging.
- Do not log secrets or URL query strings by default.
- UI strings must use resources.
- Business logic must not depend on Avalonia.

## Architecture boundaries

### FocusGuard.Domain

Pure business rules. No Avalonia, database, or Win32 references.

### FocusGuard.Service

Authoritative for session state and expiration. Must function without the desktop UI.

### FocusGuard.Desktop

Configuration and status only. Never directly enforces policies.

### Browser extension

Receives only browser policy and does not collect unrelated browsing activity.

## UX rules

- Never use default-looking unstyled controls in final screens.
- Use semantic tokens, not hard-coded visual values.
- Preserve visible strict-mode and recovery warnings in every theme.
- Every destructive or locking action must show consequences and exact expiration time.
- Accessibility is part of acceptance criteria.

## Completion report

Each checkpoint must report:

1. Summary
2. Files changed
3. Tests added
4. Commands executed
5. Results
6. UX implications
7. Security implications
8. Limitations
9. Next task

## graphify

This project has a graphify knowledge graph at graphify-out/.

Rules:
- Before answering architecture or codebase questions, read graphify-out/GRAPH_REPORT.md for god nodes and community structure
- If graphify-out/wiki/index.md exists, navigate it instead of reading raw files
- For cross-module "how does X relate to Y" questions, prefer `graphify query "<question>"`, `graphify path "<A>" "<B>"`, or `graphify explain "<concept>"` over grep — these traverse the graph's EXTRACTED + INFERRED edges instead of scanning files
- After modifying code files in this session, run `graphify update .` to keep the graph current (AST-only, no API cost)
