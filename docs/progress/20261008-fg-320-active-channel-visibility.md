# FG-320 — Salud de canales visible durante una sesión

Fecha: 2026-10-08
Estado: implementado y validado en fuente/paquete .108; aceptación visual instalada pendiente.

## Problema

FG-268 confirmó que el Host activo muestra timer y fin exacto en el primer viewport de 1216×799, pero las tarjetas `Servicio`, `Extensión` y `Protección` quedan debajo del pliegue. El DOM/UIA ya publica esos valores; el defecto es su disposición vertical.

## Decisión y plan

1. En la pestaña Resumen y sólo cuando hay una sesión activa con estado actual, presentar la tarjeta de sesión y los mismos tres indicadores en dos columnas responsivas; en anchuras menores o iguales a1000px, apilar.
2. Mantener literalmente los valores localizados actuales y la regla de ocultar hechos cuando la conexión está stale/unavailable; no fabricar estados ni tocar IPC.
3. Conservar el formulario idle y mantener una vista completa de salud en Conexión.
4. Añadir regresiones para la ubicación del panel de salud y verificar CSS de producción, viewport720×480, CSP, contraste, React y Host.
5. Regenerar el Setup versionado .108; no instalarlo ni cambiar perfil/sesión.

## Aceptación

En una sesión Active fresca, los tres indicadores aparecen como hermanos visuales del panel de sesión y no quedan al final de la página a1216px; a≤1000px vuelven a un flujo vertical sin overflow horizontal. Los estados stale/unavailable no muestran indicadores afirmativos obsoletos.

## Implementación

- `src/FocusGuard.UI/src/App.tsx`: `summary` envuelve el panel de sesión y los facts en `session-overview`; sólo aplica la clase de dos columnas cuando `active && current`. La pestaña Conexión conserva sus tres hechos.
- `src/FocusGuard.UI/src/design-system/tokens.css`: sesión a la izquierda, salud compacta a la derecha sobre1000px; flujo vertical a1000px e inferior; mantiene el borde visible en forced-colors.
- `src/FocusGuard.UI/src/App.test.tsx`: Active ubica tres facts junto al panel; al pasar a stale, desaparecen.
- `src/FocusGuard.UI/scripts/verify-dist.mjs`: el build de producción exige la regla amplia y el breakpoint apilado.
- `UX_SPEC.md` y `docs/design/FOCUSGUARD_DESIGN.md`: documentan la jerarquía y el comportamiento responsivo.

## Verificación

- `npm test`: 115/115 React; `npm run build`: TypeScript/Vite, dark/light contrast, minimum-window, CSP/local assets y contract de salud activa.
- `dotnet test FocusGuard.slnx -c Release --no-restore`: 877/877 en14 assemblies; el build Host ejecutó la verificación frontend .108.
- Extensión19/19; installer static tests; Chrome/Edge package tests.
- Host .108 smokes `--smoke`, `--smoke-runtime-missing`, `--smoke-renderer-failure` y `--smoke-renderer-crash`: exit0.
- Setup .108 manifest31/31 presentes en árbol/ZIP; UI18/18 coincide con `dist`; Setup `BBEFE1E9AE892C618DC8B3ED78B273565DB79001704C0BDA08C3A49D919C2887`; ZIP `83CE140E9C191E7D4C006F92CA3DE9BC58B5809869ED6D2BECED1813F8ED1C01`. Recibo `artifacts/fg320-build-receipt.json`.

No se instaló .108 ni se capturó una nueva imagen de WebView2. La fuente/contrato de CSS y los tests pasan; la confirmación del primer viewport Active instalado sigue pendiente. Setup .102 permanece instalado, sin modificación de sesión ni perfiles.
