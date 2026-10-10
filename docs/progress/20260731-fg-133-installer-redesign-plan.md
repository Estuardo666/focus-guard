# FG-133 — Graphical installer redesign plan

## Summary

Rebuild the WinForms setup surface to match the owner-supplied centered Dark Zen
composition while preserving the existing install, uninstall, Restart Manager,
logging, and recovery behavior.

## Design plan

- Palette: Ink `#0E1318`, raised ink `#151C24`, primary text `#EDF2F5`,
  secondary text `#AEB9C4`, border `#344150`, accent `#667DFF` through
  `#9073E8`.
- Type: bundled Geist for display, body, and utility roles, with Segoe UI as the
  safe fallback.
- Layout: a custom 32 px title bar, centered shield/wordmark/action stack, and a
  quiet help/status footer. The operational log replaces the center content
  only while an action is running or after output is produced.
- Signature: the outlined shield-and-lock mark above the product name, paired
  with the same diagonal indigo action gradient used by Desktop.

## Acceptance criteria

1. The setup window closely matches the supplied 915 × 659 reference.
2. All visual values are centralized as setup design tokens derived from the
   Desktop Dark Zen resources.
3. Install, uninstall confirmation, blocking-process preflight, output, and
   keyboard access remain available.
4. The design remains legible at 100–200% Windows scaling.
5. Build, formatting, installer contracts, and relevant automated checks pass.

## Security, UX, and recovery

This is a presentation-only milestone. It does not execute the installer,
request UAC, modify services, registry, ACLs, browser policy, or the current
installation. Administrator consequences and process-closing behavior remain
visible before action. The uninstall confirmation and Restart Manager recovery
path remain unchanged.
