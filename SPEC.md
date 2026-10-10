# FocusGuard Product Specification

## Goal

Create a free, open-source Windows 10/11 application that blocks selected programs and websites during timed focus sessions.

## Components

- Avalonia desktop application
- .NET Windows Service
- shared domain and policy libraries
- native messaging host
- Chrome/Edge Manifest V3 extension
- elevated recovery utility
- installer

## Core entities

### Profile

Reusable collection of application rules, website rules, exceptions, schedule settings, and strictness settings.

### Session

Immutable activation snapshot with start, end, state, policy, and strict-mode settings.

### Enforcement policy

Normalized immutable policy consumed by enforcement components.

## MVP user stories

1. Create, rename, duplicate, and delete profiles.
2. Add an executable path, executable hash, or folder rule.
3. Add blocked domains and URL patterns.
4. Add allow exceptions with higher precedence.
5. Start a normal timed session.
6. Start an explicitly confirmed strict session.
7. See exact remaining time and enforcement health.
8. Automatically release all restrictions at expiration.
9. Recover using an elevated delayed recovery flow.
10. Review sanitized local events.

## Application blocking

Initial enforcement strategy:

1. Enumerate processes at a configurable interval.
2. Resolve executable path.
3. Match the immutable active policy.
4. Request graceful close where practical.
5. Terminate only after a short grace period.
6. Log a sanitized event.
7. Deduplicate by process instance.

The MVP must honestly document that a process may briefly launch before termination.

Protected exclusions include FocusGuard recovery components, Windows logon components, security tools, disk recovery tools, and required accessibility tools.

## Website blocking

Initial browsers:

- Google Chrome
- Microsoft Edge

Rules support:

- `example.com`
- `*.example.com`
- `example.com/path/*`
- `*://*.example.com/*`

Evaluation order:

1. browser-internal pages
2. explicit allow
3. explicit block
4. default allow

The extension must not transmit unrelated browsing history. Query parameters are excluded from logs by default.

## Timed sessions

- Minimum duration: 1 minute
- Maximum duration: 30 days
- Store timestamps in UTC
- Display local exact end time
- Activation occurs only after required components acknowledge the policy
- State transitions are idempotent

Focus cycle sessions are finite plans composed of focus, short-break, and
long-break phases. Built-in configurations are 25/5/15 every 4 cycles,
50/10/20 every 3 cycles, and 90/20/30 every 2 cycles. Custom configurations
are bounded to 1–180 minutes of focus, 1–60 minutes of short break, 1–120
minutes of long break, and a long-break frequency of 2–12 cycles.

Plans terminate either after 1–24 focus cycles or at an exact total duration
within the global one-minute to 30-day limit. A cycle-count plan ends at the
end of its final focus phase and never appends a final break. A duration plan
may truncate only its final phase and must expose that partial phase in its
preview. The domain builder is deterministic and platform-independent; the
service remains the authority that advances the active phase.

## Strict mode

Strict mode is opt-in and finite.

It should resist:

- closing the desktop UI
- ordinary reboot
- ordinary config-file edits
- wall-clock changes
- temporarily disabling the browser extension
- copying or renaming a blocked executable when hash rules are enabled

It does not promise resistance against:

- a determined administrator
- Safe Mode
- offline disk editing
- booting another OS
- recompiling the open-source application
- destructive Windows modifications

Before activation, show the profile, impacted apps/sites, exact end time, browser health, unsaved-work warning, and recovery instructions.

Required confirmation:

- generate a random two-word English phrase from the reviewed focus/calm word
  dictionary each time the strict review opens
- display the phrase plainly and require an exact ordinal match
- treat the phrase as a deliberate-action gate, not as authentication or a
  replacement for the recovery credential

First-run strict sessions are limited to four hours.

## Recovery

Before strict activation:

- generate a high-entropy recovery code
- show it once
- store only a salted cryptographic hash
- require acknowledgement that it was saved

Recovery:

- requires elevation
- includes a default 15-minute delay
- works offline
- is logged
- has Safe Mode documentation
- never depends on the desktop application

## Storage and IPC

- SQLite for structured local data
- cycle presets are stored separately from profiles; the three built-in presets
  are seeded and immutable, while custom presets are user-owned
- per-profile cycle settings store one default preset and separate Block-only
  application and website exceptions that stay blocked during authorized breaks
- the profile schema migrates additively from existing simple-session databases
- `%ProgramData%\FocusGuard` for machine state
- `%LocalAppData%\FocusGuard` for user state
- atomic policy snapshots
- DPAPI for secrets
- named pipes with restrictive ACLs
- version 2 JSON messages for prepared-session activation, phase status, and
  normal-mode break skipping; unknown fields are rejected
- bounded payload sizes and timeouts

Cycle policy snapshots include the complete phase plan, normal enforcement
rules, Block-only break exception rules, the authorized phase index, and the authorized
phase end. Integrity validation covers every field. During a current
service-authorized short or long break, consumers suspend the normal focus
rules and select only the break exception set, keeping those configured targets
blocked. At the exact break end they select
the normal focus rules again without user action. Consumers retain focus rules
when break authorization is absent or stale. After restart, the service restores the plan and reauthorizes only
the phase that is valid at the current UTC time. Invalid snapshots fail open,
are quarantined, and are logged.

Cycle activation uses a prepare/activate handshake. Preparation compiles the
plan without applying restrictions, returns the exact UTC phase boundaries,
expires after two minutes, and may be activated once. In normal mode the
service accepts a skip only for the current break and shifts the remaining
plan earlier. Strict mode rejects skip and normal stop requests.

## Desktop workflows and weekly schedules (FG-163)

Desktop remembers the last accepted profile, duration, timer/cycle mode and cycle preset.
Optional objectives and end-of-session reflections stay in a bounded local user journal;
they are never sent to enforcement consumers. Session history stores actual UTC phase
spans additively, excluding breaks from focus totals and retaining elapsed portions of
skipped normal-mode breaks. Legacy rows retain their recorded minutes without invented
phase spans. Today reads up to 2,000 recent sessions overlapping a validated UTC interval
of at most two days, with at most 128 recent timeline spans in bounded control IPC.

Weekly schedules are explicit, initially disabled, finite normal timer sessions only.
The service owns execution independently of Desktop, using a persisted rule/enforcement
snapshot, local weekday/time and a validated Windows time zone. The client shows exact
next start/end and blocking consequences before enable. Maximums: 12 schedules, 720
minutes per occurrence. Enabled overlapping schedules are rejected. Editing a profile
does not silently change an existing scheduled snapshot; it must be saved again explicitly.

Every occurrence is durably claimed before activation and never replaces an active
session. A late service wake-up may start a still-valid occurrence with at least one
second remaining. The scheduled session is clipped to the original deadline and can
therefore last less than one minute; occurrences with less than one second remaining
are skipped. The one-minute minimum remains for manually started sessions. Enabling
mid-occurrence waits for the next occurrence. Claims survive restart, stop and clock
rollback; normal sessions are not restored after reboot. Invalid persistence starts no
scheduled sessions. DST gaps are skipped, ambiguous times use the standard offset once.
See ADR 0013 for persistence and failure semantics.

Normal sessions are never restored after a service restart or reboot. When the service
stops gracefully (restart, update, Windows shutdown) an active normal session is closed as
interrupted: it is written to session history as not completed, a `SessionInterrupted`
audit event is recorded and enforcement is released. If the desktop UI observed the
session running and the service later reports no session, it shows a persistent,
dismissable notice pointing to Activity. While a normal session runs, the service also
refreshes a bounded journal (`normal-session-journal.json` beside history.db) every 10 s;
if the process crashes or is killed, the next start records the journaled session as
interrupted, ending at its last heartbeat, with a `ServiceTerminated` audit outcome, and
removes the journal. Replays are idempotent and corrupt journals are quarantined, never
recorded. The installer configures bounded service recovery (restart after 5 s and 30 s,
no action on a third failure within a day). Strict sessions are unaffected and keep their
persisted snapshot.

## Non-functional requirements

- idle service CPU target below 1%
- idle memory target below 100 MB
- process detection median below one second
- URL rule evaluation below 10 ms at 10,000 rules
- keyboard navigation and screen reader support
- high contrast support
- no telemetry by default
- Spanish and English resource architecture

## Out of scope for MVP

- kernel drivers
- VPN filtering
- TLS interception
- packet inspection
- cloud accounts
- remote control
- parental or employee surveillance
- blocking Task Manager, Registry Editor, or security tools
- “unbreakable” uninstall prevention
