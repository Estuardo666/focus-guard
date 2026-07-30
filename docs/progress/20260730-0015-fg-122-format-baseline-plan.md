# FG-122 Formatting Baseline Plan

## Summary

Reconcile the existing C# formatting drift in a dedicated, reviewable commit
before adding functionality. The change is limited to line endings and formatter
whitespace; it must not alter product behavior.

## Relevant requirements

- Keep warnings as errors and preserve the existing CI format gate.
- Never skip a failed validation command.
- Make the smallest complete milestone and record recovery implications.
- Do not change application, service, installer, or machine state.

## Current evidence

`dotnet format FocusGuard.slnx --verify-no-changes` fails with:

- Thirteen tracked C# files using LF while `.editorconfig` requires CRLF.
- One formatter indentation finding in
  `src/FocusGuard.Setup/RestartManagerPreflight.cs`.

The repository was clean before reproducing the failure.

## Implementation plan

1. Run the repository formatter without changing analyzer severity.
2. Add a repository C# EOL attribute so CRLF is reproducible on non-Windows
   checkouts.
3. Audit the resulting Git diff and confirm it contains no token-level or
   behavioral changes.
4. Run format verification, build, and the complete automated test suite.
5. Refresh Graphify because tracked code files changed mechanically.
6. Record the checkpoint, mark FG-122 complete, and commit independently.

## Acceptance criteria

- `dotnet format --verify-no-changes` passes.
- Build passes with warnings treated as errors.
- All .NET and browser-extension tests pass.
- The source diff is formatting-only.
- Graphify is current and the final worktree is clean.

## Security implications

None expected. Enforcement, IPC validation, persistence, and recovery logic
must remain token-equivalent.

## UX implications

None expected. No XAML or user-visible strings are in scope.

## Recovery implications

The FG-121 baseline commit remains the rollback point. This formatting milestone
will be a separate commit that can be reverted without affecting functionality.
