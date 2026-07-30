# ADR 0003: Safe autopilot boundary

## Status

Accepted.

## Decision

Unattended Codex work may implement pure logic, UI, simulators, tests, and documentation. It may not perform privileged installation or real blocking.

For an explicitly authorized owner-present milestone, Codex may directly execute the scoped machine-changing commands, collect errors, diagnose failures, retry safe fixes, and verify rollback when its current process already holds the required privileges. It may not auto-elevate, automate UAC or credentials, or weaken the security model to overcome an execution failure.

## Rationale

A bug in real enforcement can interrupt work or make recovery difficult. Privileged milestones require an owner-present review and rollback plan.

Requiring the owner to type every command prevents Codex from closing the diagnostic loop without adding security when the Codex process is already elevated. The meaningful boundary is explicit authorization, scoped targets, existing privilege, and recoverability—not who presses Enter.
