# FG-151 enforcement UX regression plan

## Symptoms and confirmed causes

- Every timer text update and several button-driven ring updates post an explicit
  `FocusScrollViewer.Offset = (0, 0)`, so the workspace jumps to the top.
- The installed profile used by the reported sessions (`2`) contains no application
  or website rules. The active UI nevertheless labels the empty policy as real
  blocking and can transiently classify it as degraded, so the user reasonably
  expects enforcement that the policy cannot perform.
- The installed ProcessAgent and Chrome native host are currently alive; Chrome is
  loading extension 0.1.1 from the installed staging directory, and its integrity
  script matches the packaged copy.

## Implementation plan

1. Reset the workspace scroll only for genuine workspace transitions, never for
   timer/ring copy updates or reduced-motion changes.
2. Add a localized, explicit no-rules active state and keep it separate from both
   protected and degraded enforcement.
3. Ensure the complete enforcement explanation wraps and remains available as a
   tooltip at constrained widths.
4. Add regression tests for scroll-reset classification and active enforcement
   presentation, then run formatting, build, focused tests, and the full suite.

## Security, UX, and recovery implications

- Security: no acknowledgement, integrity, lease, expiration, or fail-open rule is
  weakened. An empty policy is never described as protected or degraded enforcement.
- UX: timer ticks and duration controls preserve scroll position; the active card
  tells the user that the selected profile has no blocking rules instead of implying
  an unavailable channel.
- Recovery: service authority and all finite recovery paths remain unchanged. This
  slice changes only Desktop presentation and interaction behavior.

## Acceptance criteria

- Ring value/caption changes do not request a scroll reset.
- Entering/leaving active mode or switching Timer/Cycles may reset to the workspace
  top intentionally.
- An active, enforcement-enabled profile with zero rules shows a localized no-rules
  state, not `Protected`, `Degraded`, or `Timer only`.
- Existing protected, degraded, and explicit timer-only tests continue to pass.
