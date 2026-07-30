# Graphify integration checkpoint

## Plan

1. Verify the installed `graphify` executable and Python package.
2. Detect the repository corpus, excluding generated graph output from analysis.
3. Run deterministic AST extraction for the C# and TypeScript codebase, then build the graph outputs.
4. Validate the generated JSON/HTML/report artifacts and record security, UX, recovery, and limitations.

## Scope and implications

- Security: graphify is read-only against source inputs; generated artifacts may expose code structure and must remain local.
- UX: no product UI or enforcement behavior changes; this adds developer-facing analysis artifacts only.
- Recovery: generated output can be regenerated or removed; no FocusGuard runtime state is changed.
