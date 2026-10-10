# FG-140 — persistence plan

## Acceptance criteria

1. SQLite schema version 3 adds cycle presets, profile defaults, and separate
   application/website break-exception tables.
2. Existing v1/v2 profile databases remain readable and are upgraded without
   losing profiles or rules.
3. The three built-in presets are seeded with stable IDs and cannot be deleted
   through the store API.
4. Custom presets can be saved, listed, loaded, and deleted; deleting a custom
   preset clears any profile default that references it.
5. Profile cycle settings round-trip their default preset and allow-only break
   rules.
6. Block actions are rejected before persistence.

## Security, UX, and recovery implications

- Security: break exceptions are stored in a separate table and never mutate
  the profile's normal block rules. The store accepts only `Allow` actions.
- UX: presets can be duplicated by saving a new non-built-in ID, while built-in
  names and values remain stable.
- Recovery: schema migration is additive. No active session policy is changed;
  strict-session plan persistence remains FG-142.

## Implementation order

1. Add storage records and interface operations.
2. Add additive schema migration and built-in seed rows.
3. Implement CRUD and profile settings round trips.
4. Add real SQLite migration, CRUD, cascade, and rejection tests.
