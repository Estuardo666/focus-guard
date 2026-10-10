# Owner UI/UX adjustments — implementation plan

## Scope

Apply the owner-requested Focus workspace refinements without changing session,
enforcement, strict-mode, or recovery behavior.

## Plan

1. Keep the Focus scroll position stable while Timer/Cycles and enforcement state change.
2. Replace whole-timer crossfades with per-character timer transitions and fit long values inside the dial.
3. Make the Focus title an in-place profile selector, locked while profile changes are unsafe.
4. Prevent preset clipping at both horizontal edges and crossfade Timer/Cycles content.
5. Move Start focus above the enforcement/strict card.
6. Replace the FocusGuard flyout with persistent top navigation.
7. Add regression tests, format, build, test, visually verify, update `TASKS.md`, and refresh graphify.

## Security, UX, and recovery implications

- Security: profile switching remains governed by `CanNavigateToEditors`; strict and enforcement bindings are unchanged.
- UX: no state change may reset scroll; reduced-motion continues to bypass transitions; controls retain keyboard and automation labels.
- Recovery: no persistence, service, policy, installer, or recovery path is modified. The changes are presentation-only.

## Acceptance criteria

- Switching Timer/Cycles or either toggle preserves scroll offset.
- Only changed timer characters fade; the caption and unchanged characters remain stable.
- Long timer values remain legible with breathing room inside the ring.
- Profile selection happens on the Focus screen.
- Cycle and duration edge cards are fully visible.
- Start focus precedes the enforcement/strict card.
- FocusGuard, Focus, Profiles, Schedule, and Activity are directly visible in the top bar.
