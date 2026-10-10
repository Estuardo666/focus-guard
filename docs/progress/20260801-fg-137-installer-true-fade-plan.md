# FG-137 — Installer true fades, app-parity buttons, and bottom glow

## Scope

Refine the graphical installer so every screen/container appearance uses a true
opacity fade, installer buttons reproduce the installed Desktop button states,
and the main background receives a restrained center-bottom gradient glow.

## Plan

1. Add reusable bitmap fade/crossfade layers for WinForms child containers.
2. Crossfade the installer main and success screens, fade the log container on
   first appearance, and use the same transition when recovering from a failed
   Desktop launch.
3. Replace the Setup button padding nudge with the Desktop interaction model:
   160 ms eased hover/press interpolation, 1.015 hover scale, 0.985 pressed
   scale, pressed opacity, semantic hover/pressed/disabled gradients, and a
   visible focus ring.
4. Paint a semantic radial glow centered below the content over the existing
   dark base gradient.
5. Expand smoke assertions, format, build with warnings as errors, run installer
   contracts, update graphify, and regenerate the package.

## Acceptance criteria

- Main-to-success and success-to-main changes are true two-layer crossfades.
- No child container appears abruptly during the installation flow.
- Button pointer, press, disabled, and keyboard-focus behavior matches the
  installed Desktop design tokens and remains accessible.
- The background glow is subtle, center-bottom, static, and token-driven.
- Reduced-motion preference completes transitions immediately.
- Release build, smoke test, and installer contracts pass.

## Implications

- **Security:** no installer privilege, process-closing, script, or launch scope
  changes.
- **UX:** the installer gains visual continuity and control parity while keeping
  motion under the documented 120–220 ms range.
- **Recovery:** launch failure crossfades back to the diagnostic state so the
  error and log remain available.
