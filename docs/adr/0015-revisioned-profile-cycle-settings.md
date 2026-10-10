# ADR 0015: Revisioned profile cycle settings

## Status

Accepted for FG-217.

## Context

Cycle settings contain a profile's default preset and its Block-only application
and website exceptions for breaks. The cycle settings tables had unconditional
whole-record saves. A React editor layered on top of those writes could silently
replace changes made by Classic or a second window.

## Decision

Use a dedicated semantic revision over the profile id, default preset id, and
canonical break exception rows. SQLite loads and conditionally replaces the
settings in one immediate transaction. Both React and the Classic editor use
this compare-and-swap path; a stale writer reloads the current values and does
not retry its mutation. The ordinary profile revision remains scoped to the
profile's name, accent, and normal rules.

The Host exposes only bounded, typed add/remove operations for Block-only break
exceptions. It checks the owner write capability, a fresh inactive service
state, the current document, and one-use operation ids before saving. The UI
never submits arbitrary actions, SQL, paths, or service policy.

## Consequences

Both editors preserve the default preset and the other rule set when changing a
single break rule. Concurrent changes return a visible conflict instead of
overwriting the winner. Existing rows are not rewritten until the user changes
them.
