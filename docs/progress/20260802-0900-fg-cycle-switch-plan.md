# FG-143 — Timer/Cycles switch and cycle selector plan

## Summary

Reproduce the owner-approved Timer/Cycles switch, cycle preset selector, and active-cycle status composition from the supplied references. The profile accent remains the semantic source of colour.

## Plan

1. Replace the inactive mode `ListBox` with one accessible two-option switch whose shared selection surface slides between Timer and Cycles.
2. Restyle the three built-in cycle choices as a centered, equal-width row with reference-specific icons, theme-driven selection, restrained hover, and pressed feedback.
3. Keep Timer presets and Minutes visible; hide Minutes in Cycles; keep the custom-cycle action visible as a localized “Coming soon” affordance.
4. Center the active cycle card without side icons and add a continuous, theme-driven phase timeline that fills the current focus/break block smoothly.
5. Add presentation/state tests, run formatting/build/tests, and perform visual review where the local service state permits.

## UX implications

- The mode change preserves spatial continuity through a 220 ms sliding selection surface and crossfading labels.
- Keyboard focus uses the active profile accent and Windows scaling retains a single horizontal cycle row at the existing minimum window width.
- Reduce Motion removes translation while retaining immediate state and colour feedback.

## Security and recovery implications

- No enforcement, strict-mode, persistence, IPC, or recovery behaviour changes.
- Active timing remains service-authoritative; the new timeline is presentation only.

## Acceptance criteria

- Switch and cycle cards match the latest owner reference in proportion and hierarchy.
- No hard-coded colours are added to views or local styles.
- Timer Minutes stays visible; Cycles Minutes is hidden.
- The custom-cycle control communicates “Coming soon” without entering the unfinished editor flow.
- Timer sessions do not show a cycle card; cycle sessions show centered copy and no side icons.
- Motion is sub-300 ms, interruptible, and respects Reduce Motion.
