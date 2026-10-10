# FG-318 — Eliminar marca repetida del encabezado

Fecha: 2026-10-08

Estado: cambio localizado y empaquetado en .105; instalación/aceptación visual pendientes.

## Hallazgo y cambio

La captura instalada .102 repite `FocusGuard` en el wordmark fijo del riel y `FOCUSGUARD` en mayúsculas inmediatamente encima del título de cada página. El segundo rótulo no distingue una sección ni una condición.

Se elimina sólo el rótulo repetido del `page-header`. El título, el subtítulo y el wordmark permanecen. El eyebrow `review estricto · paso/2` se conserva porque informa un progreso secuencial real. No cambia navegación, texto de botones, acciones ni permisos, y no se añade movimiento.

## Decisión de diseño

La elección sigue las guías aplicadas de Taste y Frontend Design: quitar un micro-rótulo que no aporta información en lugar de añadir decoración. Emil y Apple Design favorecen la respuesta inmediata en una pantalla de escritorio; el cambio es estático. Se conserva Geist, la paleta semántica, el riel y la composición establecida.

Captura antes: `artifacts/fg305-output/fg312-installed-0.1.102-1216x799.png`. Captura posterior pendiente: Orca había devuelto `runtime_access_denied` (EPERM); no se afirma una comparación visual del .105 hasta poder abrirlo.

React114/114, TypeScript/Vite, contraste dark/light, CSP, recursos locales, viewport mínimo720×480, tests estáticos del instalador y paquetes Chrome/Edge pasaron. Los cuatro smokes del Host .105 terminaron exit0; `index.html`, `host.js`, `host.css` y `ProfileEditor.js` coinciden por SHA-256 entre `dist` y el payload. Receipt: `artifacts/fg318-output/fg318-build-receipt.json`. .105 no está instalado.

El preflight read-only del rollback acepta .105 contra el estado instalado .102 (31 archivos, Service Running como LocalService, sesión Expired/0, sin Strict) y registra `MachineStateChanged=false`: `artifacts/fg318-output/fg318-rollback-preflight.json`. No ejecuta el instalador ni el failpoint.
