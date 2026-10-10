# FG-384 — scroll del Host al iniciar sesión

## Causa y corrección
FG-383 mostró en el Host .133 que, al confirmar desde el formulario, el `SessionId` pasaba de null a uno nuevo pero `App.tsx` solo restablecía scroll al montar o al terminar. La transición Inactive→Activating/Active dejaba el mismo `document.scrollingElement.scrollTop` del formulario; el título aparecía cortado. `App.tsx` ahora resetea el scroll al detectar un SessionId nuevo y conserva el reset terminal. El cambio no modifica IPC ni lógica del servicio.

## Pruebas y paquete
- Agregué regresión a `App.test.tsx`: comienza Inactive, fija `scrollTop=360`, recibe Activating con ID nuevo. La prueba falla antes (360) y pasa después (0).
- React145/145; `npm run build` (TS, Vite, CSP, contraste, layout y `verify-dist`) pasa; extensión19/19 y `test-installer.ps1` pasan.
- Setup .134 standalone `--package-self-test` exit0; manifest31/31. Setup SHA `B37F5BDEC622DD0F267203FE828DD1CFDA86AB2FEB35772E6B91B8C306AB337C`; ZIP SHA `C8AE81814F6E473C0A5B60A6A39A15AFEDD6704D5EBCA258EBE08EE0364D2EC7`.
- Instalación autorizada .134 exit0; health check autenticado pasa como LocalService; 22/22 archivos `publish` instalados coinciden con el paquete.

## Aceptación instalada
Timer normal de1min, perfil2, Enforcement OFF, Strict OFF. Se confirmó tras desplazar el documento en la revisión. Captura activa a1216×799 muestra encabezado completo, cuenta atrás00:00:45, Servicio Conectado, Extensión Disponible y «No solicitado». Diagnostic confirma SessionId `b17a855e-7572-48aa-83b1-840b252c781e`, EndUtc `2026-10-09T20:51:14.4417866Z`, enforcementConnected=false; expiró naturalmente en ese fin. Preferencias restauradas exactamente al SHA/LastWrite originales. La sesión de prueba dejó un segundo registro en Activity, conservado.

Evidencia: `D:\TEMP\FocusGuard-FG384-active-session-scroll-20261009\active-session-header-visible-0.1.134.png`, `active-session-header-visible-status.json` y `status-natural-expiry.json`.

## Siguiente gate
Editar/guardar perfil y regla en ventana estrecha sin alterar los datos originales; después repetir continuidad con enforcement real/Agent y comprobar pérdida/reconexión. Fase10 sigue pendiente.