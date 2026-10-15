# Phase 3 local overrides integration — plan/checkpoint

Date: 15 October 2026.

## Slice

Persist per-device theme, UI language, and default-duration overrides inside the already DPAPI-protected, issuer/account/generation/device-scoped settings outbox. Keep these values out of the global `SettingsWriteIntent` and encrypted document. Make every projection path compose the current draft/verified remote baseline with durable overrides, and allow an optimistic, verified local update/clear operation without enabling consent or waking the network runtime. When an edited selected field is locally overridden, update its override; publish only selected fields without a local override.

## Implementation plan

1. Extend the local outbox with backward-compatible empty overrides and preserve them through receive/resolve transitions.
2. Compose durable and existing explicit projection overrides, with explicit call-site values retaining precedence.
3. Add a stale-state-guarded local override update API and route selected edits by field; wake automatic sync only when the shared draft/tombstone changes.
4. Add focused Core/Windows sync tests for DPAPI restart, projection on remote updates and clears, selected edits, no-cloud override-only updates, and stale updates.

## Checkpoint

Security boundary: overrides are local-only state protected by the scoped current-user DPAPI vault; cloud envelopes continue to contain only the three-field global allowlist. UX boundary: override removal immediately reprojects the newest draft/verified global state. Consent remains opt-in and changing an override alone must not schedule sync. Validation is limited to the relevant Windows sync test project; no installer, graph, or staging work is part of this slice.

## Result

- `SettingsOutbox.LocalOverrides` defaults to an empty value for older serialized outboxes and travels with receive/resolve state transitions. The existing current-user, per-device DPAPI vault protects it across restart.
- `DesktopSettingsProjection.ApplyOutbox` composes durable overrides with the existing explicit argument; explicit call-site values keep their previous precedence. Remote ticks, conflict projection, local edits, and reapply therefore use the current durable choices.
- `LocalSettingsCoordinator.UpdateOverridesAsync(expectedRevision, expectedOverrides, selectedOverrides)` verifies that the reviewed revision and prior selection are still current, persists or clears choices, and immediately reprojects. `SettingsAutomaticSyncCoordinator.UpdateOverridesAsync` exposes that local-only operation without notifying the runtime.
- Selected edits update already-overridden fields locally and put only unoverridden selected fields in the global draft. Automatic edit wakeups now occur only when the shared draft/tombstone changes.
- The global write test confirms `SettingsWriteIntent` contains only the global draft even when local overrides exist. No consent bit is changed by override updates.

## Files changed

- `cloud/sync/Core/SettingsOutbox.cs`
- `cloud/sync/Core/SettingsReconciliation.cs`
- `cloud/sync/Windows/DesktopSettingsProjection.cs`
- `cloud/sync/Windows/LocalSettingsCoordinator.cs`
- `cloud/sync/Windows/SettingsAutomaticSyncCoordinator.cs`
- `cloud/sync/Tests/LocalSettingsCoordinatorTests.cs`
- `cloud/sync/Tests/SettingsOutboxTests.cs`
- This checkpoint.

## Validation

- Command: `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore`
- Result: passed, 156/156 tests (including actual Windows DPAPI restart coverage).
- UX: clearing one field uses the latest verified global/draft value on this device; other per-device selections remain active.
- Security: local selections remain within the scoped DPAPI outbox, never enter the E2EE write intent, and do not grant sync consent or trigger a network wake on their own.
- Downgrade boundary: once a build containing this change next saves the outbox (including with an empty override selection), `.174`'s strict unknown-field reader cannot read the new `LocalOverrides` property. It fails closed and leaves the DPAPI file intact; return to the newer build to resume sync. Do not edit/delete the file or strip the new field during rollback.
- Limitations at the Core checkpoint: Host/UI and installer validation had not yet run; no staging or two-PC physical acceptance was run in that slice.
- Next after the integration checkpoint: complete the parent-owned `.175` package checks, then run physical two-PC acceptance.

## Host, bridge, and UI integration checkpoint

The native reviewer now opens through `SettingsLocalOverridesDialog.ShowForReview(owner, reviewedState)` and returns only the three reviewed values or null on cancel. The bridge method is `account.settings.sync.overrides` with exact empty parameters; all selected values stay inside the native dialog and Host callback. The Host requires an authenticated, linked, consented session in stable `Enabled` state, serializes the operation with the existing settings-sync lifecycle, and passes revision, previous overrides, and the reviewed effective values into the update API. A draft change while the dialog is open is rejected. Cancel leaves the outbox untouched; successful local changes do not start or wake networking.

The account snapshot exposes only booleans for whether each field is held locally. The account card displays per-field local/follow status and shows the action only for enabled stable sync. English and Spanish native resources describe the effective and shared/draft values shown by the reviewer. The lazy consent-session check now uses the configured isolated test root, and portable import/export reads persistent settings through strict validation when a file exists so corruption cannot fall back to defaults.

Focused and full validation reported by the parent coordinator:

- Sync suite: 160/160 passed, including tombstone/reset precedence and same-revision stale-draft rejection.
- `HostLocalSettingsOverridesTests`: 1/1 passed, covering cancel, accept, stale review, zero transport reads/writes, and rehydrated summary projection.
- Host Windows suite: 258/258 passed.
- UI suite: 248/248 passed, including the local-override card visibility and action call.
- The parent coordinator is running the versioned `.175` package/installer checks separately; this checkpoint does not claim those checks or physical two-PC acceptance.

Updated files for the Host/UI slice include `src/FocusGuard.Host.Windows/Hosting/SettingsLocalOverridesDialog.cs`, `HostAccountService.cs`, `MainForm.cs`, `BridgeRequestParser.cs`, `HostStrings.resx`, `HostStrings.es.resx`, `src/FocusGuard.UI/src/AccountPanel.tsx`, `native/account.ts`, English/Spanish locales, and focused Host/UI tests.
