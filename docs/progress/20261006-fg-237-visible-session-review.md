# FG-237 — Revisión de sesión visible en el encabezado

## Hallazgo visual

La captura real del Host .78 a 1216×799 mostraba el formulario `Nueva sesión`, pero el único `Revisar sesión` quedaba después de los campos y por debajo del primer pliegue. En modo consulta, `Vista de consulta` aparecía junto al título y la explicación de piloto requerido quedaba al final del formulario. Esto separaba el estado de solo lectura de la acción que estaba deshabilitada.

## Cambio

- El botón único `Revisar sesión` se movió junto al encabezado `Nueva sesión`.
- El botón envía el mismo formulario mediante `form="focusguard-start-session"`; el flujo sigue siendo preparar → revisar → confirmar.
- En modo consulta, la razón concreta se coloca junto al botón y se enlaza con `aria-describedby`.
- Se quitó el botón submit duplicado del final del formulario. La revisión sigue siendo distinta de la confirmación del servicio.
- Responsive: encabezado con acción en escritorio y controles apilados por debajo de800px; forced colors conserva el borde.

## Verificación

- React97/97; TypeScript pasa. La prueba añadida después del empaquetado confirma que el botón externo se asocia al formulario y envía `session.prepareNormal`; no activa una sesión.
- `dotnet test FocusGuard.slnx -c Release --no-restore` permanece en851/851 tras el cambio F236 anterior; el cambio FG237 sólo toca React/CSS/test.
- Extensión19/19 y check; `test-installer.ps1` y `test-browser-extension-package.ps1 -Version 0.1.79` pasan.
- Setup standalone0.1.79 `--package-self-test` exit0. SHA Setup `E5178412D36FA78AA18256FBD6A2ACFBC9660AFEE6CF5CA56E7FDCCC9EDE17AE`; ZIP `60B8D9AA8BDD2D12E24E8E3F093A2E8E4BBEF74D911A3C9CEFE99ADD40B7BF61`.
- Se actualizó graphify a10182 nodos/27487 aristas.
- Artefactos: `D:\TEMP\focusguard-fg166-validation\artifacts\fg237\FocusGuard-0.1.79-Setup.exe`, `FocusGuard-0.1.79-win-x64-menu.zip` y `build-receipt.json`.

## Aceptación instalada

0.1.79 quedó instalado: `host.css` y `FocusGuard.UI.dll` en Program Files coinciden por SHA-256 con el paquete. SCM registró 7034 a las 16:13:06 local; el owner health check dio `Ready` a las 21:13:15Z y el servicio quedó `RUNNING` como LocalService, PID44056.

- Antes: `D:\TEMP\focusguard-fg166-validation\artifacts\fg237\host-0.1.78-pilot-summary.png` dejaba el CTA bajo el formulario.
- Después, piloto: `D:\TEMP\focusguard-fg166-validation\artifacts\fg237\host-0.1.79-pilot-summary.png` muestra `Revisar sesión` junto al título y habilitado.
- Después, consulta: `D:\TEMP\focusguard-fg166-validation\artifacts\fg237\host-0.1.79-readonly-summary.png` muestra el CTA deshabilitado y la explicación adyacente.
- No se pulsó el CTA ni se inició una sesión.

## Continuidad pendiente

El CTA FG237 está visualmente verificado en .79 a 1216×799 en piloto y consulta. Fase7 aún debe probar un timer finito sin bloqueo, la segunda confirmación, cierre/reapertura con mismo `sessionId`, ciclo y bloqueo app/web. Fase9 mantiene Narrator/forced colors, Windows/DPI, recuperación y rollback.
