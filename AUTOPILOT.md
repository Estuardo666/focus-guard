# Codex Autopilot Protocol

## Objective

Advance FocusGuard overnight through safe, reviewable milestones without performing privileged installation or destructive machine changes.

## Start command

Read:

- `AGENTS.md`
- `SPEC.md`
- `UX_SPEC.md`
- `THREAT_MODEL.md`
- `TASKS.md`
- all ADRs

Then execute tasks in order.

## Loop

For each task:

1. Confirm prerequisites.
2. Create `docs/progress/YYYYMMDD-HHMM-task-id.md`.
3. Record plan and acceptance criteria.
4. Implement the smallest vertical slice.
5. Add tests.
6. Run restore, formatting check, build, and tests.
7. Fix failures before proceeding.
8. Update the task status.
9. Record files, commands, results, risks, and next task.
10. Continue to the next unblocked task.

## Stop conditions

During unattended execution, stop and leave a detailed report when:

- a task requires administrator privileges
- a task would install or start a real Windows service
- a task modifies Registry, firewall, hosts file, browser policy, boot settings, or machine ACLs
- recovery behavior is ambiguous
- specifications conflict
- tests reveal possible permanent blocking
- a dependency introduces unclear licensing
- the same failure remains after three materially different attempts
- a change would cross a safety boundary in `AGENTS.md`

## Owner-present execution

Owner-present execution is distinct from unattended autopilot. After the owner explicitly authorizes a named privileged milestone, Codex may run the approved commands directly, inspect their output and Windows diagnostics, fix failures, retry within the documented scope, and execute the rollback drill when:

- the current Codex execution context already has the required privileges
- exact targets and rollback commands are documented before mutation
- safe build and automated tests pass first
- no UAC, authentication, credential, or security UI is automated
- no account privilege or ACL is widened merely to make the operation succeed

If the current Codex process lacks the required privileges, Codex must prepare the exact full command for an owner-run elevated shell, then resume diagnostics and verification from its own session. Documentation cannot grant operating-system privileges that the process does not possess.

## Overnight safe scope

Allowed:

- domain models and tests
- UI shell and mock-data screens
- URL parser and matcher
- application path/hash matching
- process-monitor simulation using injectable fake process sources
- IPC contracts and in-memory transports
- SQLite migrations in temporary test directories
- documentation
- extension rule engine and unit tests
- build scripts and CI

Not allowed without explicit owner-present authorization:

- real process termination
- service installation
- strict persistence
- ACL modification
- scheduled tasks
- browser policy installation
- extension force-installation
- elevated recovery
- installer execution

## Quality threshold

Do not proceed when:

- build fails
- tests fail
- warnings are introduced
- accessibility labels are missing from new final UI controls
- a screen uses hard-coded theme colors instead of semantic tokens
- user-visible claims exceed demonstrated behavior

## Final overnight report

Create `OVERNIGHT_REPORT.md` containing:

- completed tasks
- incomplete tasks
- architecture changes
- screenshots or UI descriptions
- tests and results
- unresolved failures
- security concerns
- exact recommended next command
