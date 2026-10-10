# FG-143 plan — custom cycle configuration and strict plan review

## Scope

- Load cycle presets from the profile store, including user-owned presets.
- Add a bounded custom preset editor with create, update, and delete actions.
- Persist the selected preset and Allow-only break exceptions per profile.
- Prepare strict cycle sessions before showing the review and display the
  service-returned phase plan before activation.

## Acceptance criteria

- Built-in presets remain immutable and custom presets round-trip through the
  existing `IProfileStore` contract.
- Invalid custom values never reach storage or IPC; the domain validator owns
  the bounds.
- Break exceptions are stored only as Allow rules and are sent only in the
  prepared cycle request.
- Strict cycle activation consumes the already-reviewed preparation exactly
  once; cancel clears the one-time preparation and recovery state.
- New controls have localized strings and automation names.

## Implications

- Security: Desktop only edits configuration and sends the service-authoritative
  prepared plan; it never advances a phase or relaxes enforcement itself.
- UX: custom editing stays in the inactive Focus surface, with exact values and
  a readable phase preview; strict review exposes the returned UTC-derived plan.
- Recovery: an expired or rejected preparation leaves the user in review without
  activating a session, and cancellation discards the pending preparation.

## Validation plan

- Add ViewModel tests for loading, custom CRUD, profile settings, and strict
  preparation lifecycle.
- Run targeted formatting, Desktop build, focused tests, full .NET tests, and
  `graphify update .` after code changes.
