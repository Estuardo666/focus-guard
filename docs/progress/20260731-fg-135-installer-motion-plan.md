# FG-135 — Zen installer motion and branded completion plan

## Scope

Refine the graphical installer with calm, purposeful motion, replace the
remaining native blocking-process dialog with a branded in-window-style modal,
and add a dedicated successful-installation state.

## Plan

1. Extend the Setup semantic tokens with success, modal, elevation, and motion
   values aligned with Dark Zen and the Desktop strict/profile modals.
2. Rebuild the blocking-process prompt as a borderless owner-sized overlay with
   a blurred owner snapshot, dimming scrim, centered branded card, explicit
   unsaved-work warning, and animated enter/exit.
3. Add a successful-installation state with a restrained animated check/glow,
   `Salir`, and `Aceptar y abrir FocusGuard`.
4. Honor Windows client-area animation preferences by completing transitions
   immediately when motion is disabled.
5. Expand the Setup smoke test, run formatting/build/installer contracts, update
   the knowledge graph, and regenerate the self-contained installer artifact.

## Acceptance criteria

- Modal and completion controls use Setup semantic tokens and no default white
  WinForms surfaces.
- Modal entry and exit complete within the UX-spec 120–220 ms range and never
  loop.
- The blurred/dimmed owner is non-interactive while the modal is open.
- Cancel preserves running applications; confirmation keeps the existing
  reviewed close-process flow.
- Successful installation offers both exit and verified installed-app launch.
- Reduced-motion preference removes decorative interpolation without removing
  state feedback.
- Release build, warnings-as-errors, smoke test, and installer contracts pass.

## Implications

- **Security:** no process-closing scope or privilege behavior changes; the
  process list remains explicit before confirmation.
- **UX:** motion explains modal/state changes without delaying frequent actions;
  success remains readable without relying on color.
- **Recovery:** cancel and failed launch keep the installer open with actionable
  status/log output; installation rollback remains owned by the existing script.

