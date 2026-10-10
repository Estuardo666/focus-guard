# FG-207 — React session-end and cycle-phase presentation

Phase 8 continuation. Connect the new React host to the existing shared `SessionEndPresentation` and `CyclePhaseNotifications` preferences.

## Shipped

- The Settings view now exposes all four shared session-end choices and the cycle-phase notice toggle. Their descriptions identify that these notices require FocusGuard to remain open.
- A live session transition from `Active`/`Activating` to a terminal state loads the authoritative service summary for that same session. `Window` displays an accessible native HTML dialog, `DimmedWindow` uses a stronger backdrop, `Notification` shows an in-app notice that can open the summary, and `None` does not load a summary.
- Cycle notices appear when consecutive ready snapshots from the same `FocusCycle` session move into focus or a short break. The event is deduplicated by session and phase index. Stale/disconnected state and the first snapshot after reconnect reset the baseline, so old events are not replayed.
- Summary capture controls remain guarded by the existing fresh inactive-state and pilot checks. “Another session” only prefills the recorded duration and returns to normal review; original profile, mode, enforcement, and cycle configuration are not fabricated.
- Installer build scratch paths are scoped to the requested version. Existing `setup-payload/0.1.54` and earlier setup-publish outputs were preserved during the 0.1.55 package build.

## Validation

- `npm run build` passed in the D validation copy and E shared checkout: strict TypeScript, Vite production bundle, local-chunk manifest and CSP/resource verifier. `host.js` 495.89 kB; local chunks are unchanged in count and remain bundled separately.
- `dotnet build FocusGuard.slnx --configuration Release --no-restore` passed from the E shared checkout with 0 warnings and 0 errors.
- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.55` completed. It validated embedded hashes for Desktop and all WebView files, including React `host.js` and local chunks.
- Standalone installer: `artifacts/fg207/FocusGuard-0.1.55-Setup.exe`, SHA256 `E4AC571A72F3124E81A88C61F5F02AC794DE55111D59151B9E8F839E25709428`.
- Menu ZIP: `artifacts/fg207/FocusGuard-0.1.55-win-x64-menu.zip`, SHA256 `1BB01DE0DD62B2E5E40E41EA847D1E185F41B6627957FE4CB437CFCC44BB4749`.
- No tests were run and no product installation or owner/service/browser/session state was changed.

## Still open

System notifications while the UI is closed, exact session replay metadata, strict/recovery and expanded history, break-rule policy decision, profile themes, compact/tray/help parity, and installed acceptance across Phases 4–10 remain open. This package has not been installed.
