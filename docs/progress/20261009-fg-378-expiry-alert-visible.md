# FG-378 — hacer visible el aviso de revisión vencida

## Síntoma y root cause

En la aceptación instalada .129, el aviso `reviewUnavailable` se generaba correctamente y tenía `role=alert`, pero React lo renderizaba después del formulario largo. A 1216×799, el texto quedaba fuera del primer viewport; la persona solo veía el formulario de «Nueva sesión» y no el motivo por el que la revisión había expirado.

Una prueba de orden DOM confirmó la ubicación: el aviso aparecía después del formulario. La aserción nueva falló antes del cambio.

## Cambio

- El error `ActivationRejected`/`PreparationUnavailable` se muestra inmediatamente después del encabezado y antes del formulario.
- Se mantiene el texto localizado, `role=alert` y el foco de vuelta en «Revisar sesión». No añadí animación ni cambié colores o tokens.
- Servicio, IPC, reglas, políticas y preferencias no cambiaron.
- El paquete pasa a .130.

## Archivos cambiados

- `src/FocusGuard.UI/src/DashboardStart.tsx`
- `src/FocusGuard.UI/src/DashboardStart.test.tsx`
- `src/FocusGuard.UI/package.json`, `src/FocusGuard.UI/package-lock.json` y `extension/manifest.json`
- `TASKS.md` y `docs/FOCUSGUARD_UI_MIGRATION_PLAN.md`
- `graphify-out/GRAPH_REPORT.md`, `graphify-out/graph.json` y `graphify-out/ast-extraction.json`

## Verificación

- La prueba nueva verifica que el aviso precede al formulario. Fallaba en .129; pasa en .130. La segunda prueba vuelve a comprobar el vencimiento automático y el rechazo de un clic al límite.
- `npm test`: 23 archivos, 144/144. `npm run build`: TypeScript, Vite, CSP, contraste, layout mínimo, forced-colors, teclado y reduced-motion pasan.
- `dotnet test FocusGuard.slnx`: 889/889 en Debug.
- `graphify update .`: AST y grafo regenerados tras el cambio de React.
- `scripts/test-installer.ps1` y `scripts/test-browser-extension-package.ps1 -Version 0.1.130` pasan.
- `scripts/build-installer.ps1 -Version 0.1.130 -OutputDirectory D:\TEMP\FocusGuard-FG378-20261009\package -PayloadSuffix fg378-review-alert-20261009 -SetupPublishSuffix fg378-review-alert-20261009` termina correctamente. Manifiesto 31/31, Setup standalone igual al incluido. Setup SHA-256 `0B7E4C6F9EC852F780843C64A1FA0B40882577C8AC813411219A423F89769FA5`; ZIP SHA-256 `842072CCE43852C1BF7828944F0E78C5DC343A3062EA2ABC2E60FADE69FF63A2`. El bundle `DashboardStart.js` coincide con el output de Vite y contiene el aviso nuevo.
- Rollback preflight .130 contra .129: servicio Running como LocalService, sesión Inactive, Strict apagado, consentimiento presente; `InstallerInvoked=false`, `MachineStateChanged=false`.
- Instalación .130 mediante la misma llamada a `install-focusguard.ps1` que usa Setup, con autorización previa y consentimiento ya registrado. Exit code 0; el script confirma salud IPC y payload verificado.
- En Program Files, 6/6 ejecutables y 16/16 recursos del Host coinciden por SHA-256 con el paquete .130.
- Host .130 abierto como usuario, ventana 1216×799: preparé una revisión normal de 1 minuto y no la confirmé. Al vencer a las 14:20:14, el panel desapareció y el aviso quedó visible junto al encabezado. Servicio `Conectado`, extensión `Disponible`, confirmación `Sin sesión activa`. Probé también 1025×701: alerta y CTA visibles, sin recorte horizontal; devolví la ventana a 1216×799. Seleccioné de nuevo Ciclos/Enfoque profundo; perfil 2, reglas y preferencias se conservaron.

Capturas:

- `D:\TEMP\FocusGuard-FG378-20261009\installed-expiry-alert-visible-0.1.130.png`
- `D:\TEMP\FocusGuard-FG378-20261009\installed-expiry-alert-visible-cycle-restored-0.1.130.png`
- `D:\TEMP\FocusGuard-FG378-20261009\installed-expiry-alert-visible-1025x701-0.1.130.png`

## Recuperación de espacio

Los binarios e intermediarios regenerados durante las pruebas quedaron en `D:\TEMP\FocusGuard-FG378-build-archive-20261009\`. El manifiesto verifica 69 directorios y 5.477 archivos con igualdad de bytes. E quedó con 135.78 GiB libres; D, con 34.68 GiB. El Setup/ZIP final .130 permanece en `D:\TEMP\FocusGuard-FG378-20261009\`.

## Estado y siguiente paso

.130 está instalado y aceptado para expiración/visibilidad del aviso. Siguen pendientes las otras pruebas de Fase 9: Edge real (el navegador conserva extensión .98), 1024×700/800×600/720×480, cambio real de DPI, Windows 11, Narrator/forced-colors, rendimiento activo/sostenido, reboot/recovery y rollback real .130→.129. La aceptación completa de las demás funciones sigue limitada a .122/.123 según el estado del plan.
