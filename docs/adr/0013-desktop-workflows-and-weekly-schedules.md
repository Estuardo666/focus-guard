# ADR 0013: Desktop workflows and finite weekly schedules

## Status

Accepted for FG-163, explicitly requested by the owner.

## Decision

Keep scheduling inside the authoritative service, with a five-second worker and the
existing validated activation path. Do not create OS scheduled tasks. Persist at most
12 schedules atomically under the existing protected ProgramData directory. Each stores
weekday mask, minute-of-day, time-zone ID, finite normal activation snapshot, explicit
enable time, latest claimed occurrence and outcome. Reject overlapping enabled entries.

Persist a claim before activation. A crash between claim and activation can skip one
session, but restart cannot replay it. Stop does not reset claims. The scheduler never
replaces an active session, never activates strict mode, and clips a late start to the
original occurrence end. Enabling during an interval waits until the next interval.
Invalid schedule files fail closed for scheduling; existing recovery stays available.
Worker errors back off 30 seconds; no unbounded replay. DST gaps skip and overlaps choose
the standard offset once. A time-zone change requires an explicit schedule save.

Use additive history phase storage to preserve old SQLite history. Today computes from
actual completed/elapsed phase evidence with UTC clipping, 2,000-record query and 128-span
IPC bounds. Add optional phase start to status for accurate current-phase clocks. Retain
elapsed break evidence when skipping; shift only the remaining break duration.

Store optional objective/reflection in a separate bounded user-local journal (240/1,000
characters; 1,000 entries; 8 MiB input bound). UI journal failures never change enforcement.
Remember accepted configuration only. Repeat resolves its original profile and asks for
fresh strict review where required. App discovery reads Start Menu shortcuts without
executing targets; local icon handles are freed. Compact UI only displays service status.

## Consequences

Desktop can close while schedules continue. New Desktop and service binaries must be
updated together for these IPC endpoints. Normal scheduled sessions are not resumed on
reboot; a claimed occurrence stays consumed. Rule snapshots are stable when profiles
change. Legacy history contributes recorded minutes without fabricated breaks. Today
summarizes ended session records; a running session enters history on closure. The week
editor intentionally schedules finite timers; cycle scheduling remains future work.
