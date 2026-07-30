# FG-122 Formatting Baseline

## 1. Summary

Restored a reproducible C# formatting gate without changing application logic.
Thirteen C# working files were normalized from LF to the repository's required
CRLF convention. Because Git stores their normalized text content identically,
there is no staged C# token diff.

Added `.gitattributes` with `*.cs text eol=crlf` so Windows and Linux checkouts
both satisfy the same format rule.

## 2. Files changed

- `.gitattributes`
- `TASKS.md`
- `docs/progress/20260730-0015-fg-122-format-baseline-plan.md`
- `docs/progress/20260730-0025-fg-122-format-baseline.md`
- `graphify-out/GRAPH_REPORT.md`
- `graphify-out/graph.html`
- `graphify-out/graph.json`

Graphify outputs changed because the first update after Git initialization
re-indexed the full tracked corpus: 354 files, 2,654 nodes, 4,509 edges, and
380 communities.

## 3. Tests added

None. The milestone changes formatting policy and generated architecture
metadata only.

## 4. Commands executed

- `dotnet format FocusGuard.slnx --verify-no-changes --verbosity minimal`
- C# line-ending audit across tracked files
- `dotnet format FocusGuard.slnx --verbosity minimal`
- `./scripts/build.ps1`
- `./scripts/test.ps1`
- `graphify update .`
- `git diff`, `git status`, and `git ls-files --eol` audits

## 5. Results

- Format verification: passed
- Release build: passed with 0 warnings and 0 errors
- .NET tests: 233 passed, 0 failed, 0 skipped
- Browser-extension tests: 14 passed, 0 failed
- C# EOL attribute: `text eol=crlf`
- C# source files in staged diff: 0
- Product behavior changes: none

The initial format verification failed as expected on thirteen LF files and one
whitespace diagnostic. After applying the formatter and the Git attribute, the
same verification command passed.

## 6. UX implications

None. No XAML, resources, or user-visible behavior changed.

## 7. Security implications

None. Enforcement, IPC, policy, persistence, and recovery code remain
token-equivalent. Their automated tests pass unchanged.

## 8. Limitations

- The EOL guarantee is intentionally scoped to C# files. Other file families
  retain their existing conventions.
- Graphify's refreshed output is substantially larger because the previous
  graph predated the complete Git baseline; this is generated metadata, not an
  architecture change.

## 9. Next task

Add a focused regression-test gate for the next selected functionality, then
implement that functionality as a small vertical slice.
