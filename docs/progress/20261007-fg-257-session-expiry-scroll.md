# FG-257 — Volver al encabezado al expirar una sesión

## Plan

Reproducir la transición de una misma sesión de `Active` a `Expired`, verificar si el documento conserva el scroll, y corregir sólo esa transición. Mantener intactos el estado y los comandos del servicio; validar con una regresión, las suites React/.NET y el paquete standalone.

## Hallazgo y causa

La captura instalada .86 de 1216×799 muestra que el Host pasó a «Nueva sesión» al expirar, pero el título de página y la marca quedaron cortados arriba del viewport. El código de `App` conserva el mismo documento y retira la superficie de sesión al estado terminal; no restauraba el scroll. El test controlado confirmó la causa: al iniciar con `scrollTop=0`, fijarlo a360 y cambiar la misma sesión de `Active` a `Expired`, el valor seguía en360.

## Cambio

`App.tsx` ahora revisa la transición en `useLayoutEffect`. Cuando la sesión anterior estaba `Active`/`Activating`, el nuevo estado de la misma SessionId es terminal (`Expired`, `Stopped`, `Recovered` o `Failed`) y el backend está `ready`, establece `document.scrollingElement.scrollTop=0` antes del siguiente paint. No altera la sesión, el foco, el scroll en otras vistas ni la comunicación nativa.

## Verificación

- La nueva regresión primero falló sin el fix: esperaba0 y encontró360. Después del cambio pasa.
- React: 105/105; TypeScript: pasa; build React de producción, CSP, assets locales y contraste pasan.
- Solución Release: 874/874; extensión: 19/19; prueba estática del instalador y fixture de paridad pasan.
- Setup standalone .89 `--package-self-test`: exit0. Host `--smoke`, `--smoke-renderer-failure` y `--smoke-renderer-crash`: exit0.
- Setup manifiesta31 archivos presentes; Chrome y Edge tienen14 entradas y coinciden con los hashes de distribution.json.
- Graphify: 10,264 nodos, 27,964 aristas, 991 comunidades. Omitió `graph.html` por superar su límite de5,000 nodos.

## Artefactos y límites

Setup .89 y ZIP menú están en `D:\TEMP\focusguard-fg166-validation\artifacts\fg258`; hashes y resultados en `build-receipt.json`. No instalé .89: Service/Desktop/Host siguen en .86 según la última lectura; Service PID30976 continúa `Ready`, la sesión documentada quedó `Expired` y el Host .86 PID32000 permanece abierto. No envié Activate/Stop ni reinicié el servicio.

La captura instalada de «antes» es `D:\TEMP\focusguard-fg166-validation\artifacts\fg257\installed-host-current.png`. No hay captura real instalada «después» porque el fix no está instalado; la regresión y el harness Edge verifican el efecto DOM antes del paint. Aunque Orca CLI no está disponible, Windows UI Automation permitió leer el Host .86 actual:89 elementos,21 controles interactivos, todos keyboard-focusable; la muestra se guardó como `D:\TEMP\focusguard-fg166-validation\artifacts\fg259\installed-uia-tree.json`. Esto verifica semántica de controles, no navegación real con Tab ni Narrator.

Como verificación visual adicional, serví una copia temporal del UI .89 en Edge headless con un puente mock: primero entregó `Active`, fijó el scroll del documento en90px y después entregó `Expired`. El DOM final reportó `scrollTop=0`; la captura muestra la marca y el encabezado completos. Verifiqué además 720×480, 1409×900 oscuro/claro y `forced-colors: active` (matchMedia true). Capturas en `D:\TEMP\focusguard-fg166-validation\artifacts\fg259`: `expiry-reset-headless-edge-1216x799.png`, `expired-viewport-720x480.png`, `expired-viewport-1409x900-dark.png`, `expired-light-theme-1409x900.png` y `expiry-forced-colors-1216x799.png`; hashes y secuencia en `ui-visual-matrix.json`. Esto verifica Chromium y recursos locales, no el WebView2/LocalService instalado.

FG-235 sigue sin causa: las líneas compartidas `RequestTimeout` de las12:24Z son cancelaciones por solicitud, unas11 horas antes del 7034 de las18:28 local.

## Siguiente paso

FG-242: volver a leer el estado de servicio/sesión justo antes de una actualización autorizada y owner-present; instalar .89 cuando corresponda, capturar la transición en Host instalado y completar aceptación de teclado, temas, forced-colors y ventanas720/1409px. FG-240 debe demostrar tráfico bloqueado real en Edge; Fase4 aún necesita aceptación LocalService/rollback.
