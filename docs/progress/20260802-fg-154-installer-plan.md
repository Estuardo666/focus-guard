# FG-154 — Package FG-153 UI/UX adjustments

## Plan

1. Record installer regeneration as a required repository workflow step.
2. Advance the package version from 0.1.2 to 0.1.3.
3. Run installer contract tests and build the Release Windows x64 standalone package.
4. Run the embedded-package self-test and non-installing graphical smoke test.
5. Record artifact paths, size, SHA-256, results, and update the task queue.

## Security, UX, and recovery implications

- Security: packaging must preserve production IPC, policy integrity, and fail-open behavior.
- UX: the package must include every FG-153 Focus workspace refinement.
- Recovery: this task builds and inspects artifacts only; it does not elevate or replace the installed application.

## Acceptance criteria

- Installer contracts pass.
- A standalone version 0.1.3 installer is generated.
- Package self-test and smoke test exit successfully.
- The final artifact hash and size are documented.
