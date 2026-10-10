# FG ring redesign — checkpoint

## Summary

Reduced the shared FocusGuard timer ring by 13% and added a two-layer numeric crossfade. The idle and active compositions continue to use the same persistent host and semantic theme resources.

## Files changed

- `src/FocusGuard.Desktop/Styles/Tokens.axaml`
- `src/FocusGuard.Desktop/Styles/Controls.axaml`
- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml`
- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml.cs`
- `tests/FocusGuard.Desktop.Tests/DesignSystemTests.cs`
- `TASKS.md`

## Tests and commands

- `dotnet format .\FocusGuard.slnx --no-restore --verify-no-changes` — passed.
- `dotnet build .\FocusGuard.slnx --configuration Release --no-restore` — passed, 0 warnings/errors.
- `dotnet test .\FocusGuard.slnx --configuration Release --no-build` — passed, 313 total tests.
- `graphify update .` — passed, 3225 nodes / 5641 edges.

## UX implications

- `FgTimerDiameter`: 240 → 209 DIP (240 × 0.87, rounded).
- `FgTimerCopyWidth`: 180 → 157 DIP.
- Center value/caption: 50/16 px → 43.5/14 px.
- Value and caption changes crossfade over the semantic 220 ms motion token; after the transition only the current copy remains visible to accessibility clients.
- Reduced motion skips the visible crossfade and switches copies immediately.

## Security and recovery implications

Presentation-only change. No IPC, service, enforcement, strict-mode, persistence, installer, or recovery behavior changed.

## Visual verification and limitation

Captured and reviewed `artifacts/ui-qa/2026-08-02/ring-redesign-idle-125pct-top.png` at the available 125% Windows scale. A real active session could not be activated in this run because the current service/pipe rejected the desktop activation request; no session or policy was left running.

## Next task

No follow-up is required for this ring refinement. A future visual pass can capture the active crossfade once the owner-present service activation environment is available.
