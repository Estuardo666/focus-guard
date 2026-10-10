# FG ring redesign — implementation plan

1. Reduce the shared timer-ring geometry and center typography by 13% through semantic tokens.
2. Add a two-layer center copy that crossfades value and caption changes without rebuilding the ring.
3. Preserve reduced-motion behavior, accessibility semantics, and dynamic theme resources.
4. Update design-system tests, run formatting/build/tests, refresh graphify, and record a visual checkpoint.

Owner correction: preserve the reduced size, but reproduce the supplied clock dial literally:
the full chromatic ring remains in both states, sixty clock indexes sit inside it, and only an
active session adds a clockwise elapsed-time gradient and luminous head. Keep the existing
localized center labels.

UX implication: idle and active states keep the same centered composition while gaining a quieter visual scale and a legible transition between countdown values.

Security implication: no service, policy, persistence, or enforcement behavior changes.

Recovery implication: the change is presentation-only and can be reverted by restoring the timer geometry and center-copy template.
