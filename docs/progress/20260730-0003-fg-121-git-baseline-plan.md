# FG-121 Local Git Baseline Plan

## Summary

Establish a local Git repository for the existing FocusGuard workspace before
adding more functionality. This milestone records the current source and
documentation without changing product logic, installed services, or machine
configuration.

## Relevant requirements

- Keep changes reviewable and checkpointed.
- Preserve recovery options before further implementation.
- Do not install services or otherwise change the machine.
- Do not version generated output, local runtime state, credentials, or
  machine-specific tool configuration.

## Implementation plan

1. Confirm that this directory is not nested in an existing Git repository.
2. Refine `.gitignore` for build output, dependencies, logs, local databases,
   machine-specific Codex state, and Graphify cache metadata.
3. Initialize a repository with `main` as the initial branch.
4. Stage the baseline and audit staged filenames and content for generated or
   sensitive material.
5. Record the completion checkpoint, create the local baseline commit, and
   verify a clean worktree.

## Acceptance criteria

- Git metadata is created only inside the FocusGuard workspace.
- The initial branch is `main`.
- Generated dependencies, build artifacts, logs, databases, caches, and
  machine-specific configuration are excluded.
- The baseline commit exists locally and the worktree is clean.
- No application, service, installation, or UX behavior changes.

## Security implications

The staged baseline must be inspected before commit. Secret-like filenames and
private-key or credential markers must not be present. Local databases and logs
remain outside version control because they may contain runtime state.

## UX implications

None. This milestone changes repository traceability only.

## Recovery implications

The initial commit becomes the recovery point for all later feature work.
Future milestones can be reviewed or reverted independently from this baseline.
