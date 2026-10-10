# Installer interactive shell launch fix — plan

## Scope

Correct the reinstall runtime restart path after Windows Explorer interpreted
the Desktop executable argument as a folder-navigation request and opened the
Documents window.

## Plan and acceptance criteria

1. Replace the `explorer.exe` command-line launch with the interactive Windows
   Shell automation API.
2. Preserve non-elevated launch intent and the existing 15-second process-path
   verification for ProcessAgent and Desktop.
3. Extend installer contracts to reject the broken Explorer command-line
   mechanism and require Shell execution.
4. Build, test, regenerate the installer, smoke-test it, update `TASKS.md`, and
   refresh graphify.

## Security, UX, and recovery implications

- Desktop and ProcessAgent remain delegated to the logged-in user's Shell and
  do not inherit Setup's administrator token.
- The installer must not open an unrelated Explorer window.
- If either process still fails to remain alive, installation continues to
  fail closed into the existing rollback path.

