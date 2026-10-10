# FG-328 — Anuncio accesible del estado de Protección

Fecha: 2026-10-08
Estado: implementado y validado en fuente/paquete .112; aceptación Narrator instalada pendiente.

## Problema

FG-326 hace que el hecho Protección cambie cuando llega `session.intent.get`. Con un lector de pantalla, el cambio de “Desconocido” a “No solicitado” o a un estado de acknowledgement podía no anunciarse.

## Plan

1. Mantener el título de la tarjeta como heading y anunciar sólo el valor Protección mediante `aria-live="polite"`/`aria-atomic="true"` durante Active/Activating.
2. No poner los otros facts en live regions, para evitar repetir Servicio/Extensión en cada actualización.
3. Añadir regresiones de active/idle para el live region sin cambiar IPC, estados, copy ni datos.
4. Ejecutar React/build, suite, graphify y preparar Setup .112; la aceptación Narrator instalada queda separada.

## Aceptación

En sesión activa el heading de Protección conserva su semántica y expone anuncio polite atómico. Inactive/terminal no queda como live region. No se anuncia cada polling porque el valor sólo cambia si cambia el estado visible.

## Implementación y verificación

- `src/FocusGuard.UI/src/App.tsx`: el valor del fact Protección sigue siendo heading y sólo añade `aria-live="polite"`/`aria-atomic="true"` cuando la sesión es Active/Activating. Servicio y Extensión no se anuncian en vivo.
- `src/FocusGuard.UI/src/App.test.tsx`: active y timer-only conservan el live region para el cambio a consentido; terminal lo desactiva.
- React118/118; `npm run build` pasa TypeScript, contraste, layout, CSP, assets y forced-colors. Suite .NET877/877 y extensión19/19.
- Setup .112 manifest31/31, UI18/18 hashes; cinco Host smokes exit0, incluyendo ventana DPI. Recibo `artifacts/fg328-build-receipt.json`.

.112 no está instalado. Falta revisar con Narrator que el cambio de protección se lea una vez y evaluar forced-colors en WebView2 real.
