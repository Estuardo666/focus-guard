# Owner-present execution policy checkpoint

## Summary

Updated the repository policy so Codex may directly execute an explicitly authorized owner-present machine-changing milestone, read its errors, diagnose failures, retry scoped fixes, and verify rollback when the current Codex process already has the required operating-system privileges.

## Files changed

- `AGENTS.md`
- `AUTOPILOT.md`
- `docs/adr/0003-safe-autopilot-boundary.md`
- `docs/progress/20260728-0925-owner-present-execution-policy.md`

## Tests added

Documentation-only policy change; no automated test applies.

## Commands executed

- Read the existing repository instructions and ADR.
- Searched the three documents for privileged-execution rules.

## Results

The distinction is now explicit:

- Unattended autopilot still stops at privileged and real-blocking operations.
- Owner-present execution may be driven directly by Codex after explicit authorization.
- Codex owns reading errors, diagnostics, safe fixes, and rollback verification.
- Existing process privilege is still required.
- Auto-elevation, UAC/credential automation, secret collection, and security weakening remain prohibited.

## UX implications

Owner-present milestones require less manual command relay when Codex is already running elevated. When it is not elevated, Codex must provide one complete command block with absolute context.

## Security implications

The change replaces the previous “explicit user execution” requirement with “explicit user authorization” but does not broaden prohibited capabilities or allow privilege escalation. Exact scope, preflight validation, rollback, and least privilege remain mandatory.

## Limitations

The current Codex process is not an administrator. This documentation change cannot grant SCM mutation rights, so the owner must still run the current FG-102 elevated commands unless Codex is restarted in an already elevated context.

## Next task

Continue FG-102 by removing the failed service entry, installing the validated `win-x64` publish, reading SCM results, and completing the rollback drill.
