# FG-314 — Explicación visible del modo de consulta en Perfiles

Fecha: 2026-10-08

Estado: corrección en fuente y paquete .103; aceptación visual e instalada pendiente.

## Hallazgo y cambio

En la captura instalada .102 de 1216×799, el riel mostraba `SOLO LECTURA`, pero la explicación `editorPilotRequired` aparecía al final del riel, debajo de la lista de perfiles y del formulario para crear uno. Se podía ver el estado sin encontrar la razón ni el siguiente paso.

La explicación localizada existente ahora aparece antes del editor de dos paneles, como una única nota semántica. Sólo aparece cuando el catálogo ya respondió explícitamente `pilotEnabled=false`; errores o carga inicial no se presentan como modo confirmado. Se conserva el mismo texto, la navegación, las reglas y las guardas de mutación. No se añadió ningún comando nativo ni se cambió el permiso del bridge.

## Decisión de diseño

Se aplicó evolución dirigida: conservar la marca y la arquitectura existente, y mover la información que desbloquea la comprensión antes de los controles. El resultado de UI/UX Pro Max apuntó a una landing de App Store con Lora/Raleway, no a una consola local; se descartó por no corresponder al producto. Se conservaron los diales documentados de Taste (varianza3, movimiento2, densidad6), y las pautas de Emil/Apple llevan a no animar una navegación frecuente ni el aviso. El texto, la superficie semántica y el borde visible comunican el modo sin depender sólo del color.

## Verificación

- `npm test`: 21 archivos, 114 pruebas aprobadas.
- `npm run build` con `focusguard-ui@0.1.103`: TypeScript/Vite y los verificadores de contraste, controles, viewport mínimo720×480, CSP y recursos locales aprobaron.
- `scripts/test-installer.ps1`: pasó.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.103`: paquetes Chrome/Edge aprobaron.
- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.103 -OutputDirectory artifacts\fg314-output\package\FocusGuard-0.1.103`: terminó con exit0; generó ZIP y Setup independiente.
- SHA-256 de `index.html`, `host.js`, `host.css` y `chunks\ProfileEditor.js` coincide entre `dist` y el payload empaquetado.
- Host empaquetado .103: `--smoke`, `--smoke-runtime-missing`, `--smoke-renderer-failure` y `--smoke-renderer-crash`, todos exit0.

La captura anterior es `artifacts/fg305-output/fg313-profiles-readonly-0.1.102-1216x799.png`. No hay captura posterior: Orca `status` y `computer capabilities` devolvieron `runtime_access_denied` (EPERM) desde este ejecutor. El Host .102 sigue instalado; no se instaló .103 ni se modificó la política UAC. Narrator, forced-colors, DPI, ejecución instalada y validación visual del cambio permanecen pendientes.

El paquete .103 de este checkpoint fue supersedido por .104, que conserva la corrección de Perfiles e incorpora FG-315. La nota continúa sin aceptación visual/instalada.
