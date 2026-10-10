# ADR 0014: Block-only cycle-break exceptions

## Status

Accepted for FG-216.

## Context

Profile storage has separate application and website exception tables for cycle
breaks. Those tables have no action column and load every entry as `Block`. The
classic profile editor creates these entries as blocks and describes them as
targets that stay blocked during breaks. The cycle IPC validator instead
accepted `Allow`, while the migrated normal and strict workflows rejected the
stored `Block` entries. This made existing profiles fail review without changing
their stored data.

## Decision

Break exception lists contain `Block` rules only. During a service-authorized
break, consumers use that exception list in place of ordinary focus rules. The
listed targets remain blocked; other focus targets are released until the exact
authorized phase boundary, when ordinary focus rules resume.

The contract validator, normal workflow, and strict workflow enforce the same
Block-only semantics. Existing persisted entries remain unchanged. The system
does not convert, drop, or infer rules during UI migration.

## Consequences

Classic and React cycle reviews can prepare profiles with existing break
exceptions. Reviews continue to disclose the exceptions, and the service remains
the sole authority for the active phase and its end. Tests cover storage,
round-trip contracts, normal/strict preparation, and the policy delivered during
an authorized break.
