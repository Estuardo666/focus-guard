# FG-326 — Copia explícita para consentimiento de bloqueo desconocido

Fecha: 2026-10-08
Estado: implementado y validado en fuente/paquete .111; aceptación visual instalada pendiente.

## Problema

FG-321 distinguió `enforcementEnabled=false` de una confirmación pendiente. Si el intent es legacy/unknown (`null`), el mapper todavía cae en “Sin confirmación” cuando `enforcementConnected=false`, lo que da a entender que el usuario sí solicitó bloqueo.

## Plan

1. Mantener tres estados inequívocos y SessionId-matched: explicitamente no solicitado; solicitado y confirmado/pendiente; consentimiento desconocido o no disponible.
2. Para unknown/missing/mismatched intent, no inferir opt-out ni confirmación de protección; mostrar estado desconocido con copy que explique el límite.
3. Mantener los estados terminales “Sin sesión activa”, stale/unavailable sin facts afirmativos, y el backend sin cambios.
4. Añadir casos ES/EN y pruebas para consent true/false/null, SessionId mismatch y channel connected/unconnected.
5. Ejecutar pruebas/UI build, suite, graphify, Setup .111 y sus smokes; no instalarlo.

## Aceptación

Sólo un intent de la misma SessionId con `enforcementEnabled=false` muestra “No solicitado”. `true` sigue el estado de acknowledgement. Null, missing o mismatched muestran “Desconocido”, incluso si el servicio marca connected; ningún estado ambiguo se presenta como opt-out, confirmación o fallo de confirmación.

## Implementación y verificación

- `protectionFactFor` ahora devuelve `unknown` con copy localizada cuando no hay intent coincidente o su consentimiento es null; un false explícito y coincidente muestra “No solicitado”.
- ES/EN incorporan el detalle `enforcementIntentUnknownDetail`.
- React118/118, suite .NET877/877 (incluye build UI TypeScript/Vite/contraste/CSP/responsive), extensión19/19; installer scripts y paquete Chrome/Edge pasan.
- Setup .111 tiene manifest31/31, UI18/18 SHA coincidente y cinco Host smokes exit0, incluido el contrato DPI. Recibo `artifacts/fg326-build-receipt.json`.

No se instaló .111. La prueba de render y el intent por Dashboard están cubiertos en fuente; la inspección visual de los cuatro estados en el Host/servicio instalado sigue pendiente.
