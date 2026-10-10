# FG-132 — Custom title-bar overlap plan

## Acceptance criteria

- Windows does not paint its native caption text over FocusGuard's custom title bar.
- The existing custom title, active-session time, drag region, and caption buttons remain present.
- The title bar continues to use the existing semantic surface and caption-button themes.

## Plan

1. Configure `MainWindow` as a decoration-free window while retaining the existing custom chrome.
2. Add a design-system regression test for the decoration boundary and custom caption controls.
3. Run formatting, build, Desktop tests, and update the graph.

## Implications

- **UX:** removes the duplicated, overlapping title while preserving familiar Windows controls.
- **Security:** no policy, enforcement, IPC, persistence, or recovery behavior changes.
- **Recovery:** reverting the single window property restores the prior native-caption behavior.
