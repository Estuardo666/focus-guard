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
- `%ProgramData%\FocusGuard` for machine state
- `%LocalAppData%\FocusGuard` for user state
- atomic policy snapshots
- DPAPI for secrets
- named pipes with restrictive ACLs
- versioned JSON messages
- bounded payload sizes and timeouts

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
