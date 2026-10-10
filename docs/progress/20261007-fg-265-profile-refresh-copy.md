# FG-265 — Copy y lectura del rail de perfiles

## Cambio

En la pantalla instalada .90, el botón del rail de perfiles y el refresh global de conexión decían ambos «Actualizar estado». La primera recarga `profiles.list` y `profile.get`; la segunda vuelve a consultar el estado del servicio. Además, en el rail de180px del Host1216px, la etiqueta del catálogo se partía en varias líneas.

El rail ahora dice «Actualizar perfiles» / «Refresh profiles» y, en ventanas de hasta1300px, apila título y acción; el botón usa todo el ancho y mantiene su etiqueta en una sola línea. El refresh global mantiene «Actualizar estado» / «Refresh status». No cambiaron protocolo, datos, reglas ni persistencia.

## Verificación de fuente y distribución

- Regresión de UI: copy ES/EN distinto del refresh de servicio y click sigue enviando `profiles.list`.
- React:107/107 tests; TypeScript y build de producción pasan.
- `verify-dist.mjs` comprueba el media rule ya minificado, el stack de editores de reglas y CSP; contrastes son ≥3:1 para bordes y ≥4.5:1 para texto/CTA en ambos temas.
- `scripts/test-installer.ps1` pasa; `scripts/test-browser-extension-package.ps1 -Version 0.1.91` pasa.
- Host publicado .91: `--smoke`, `--smoke-renderer-failure`, `--smoke-renderer-crash` exit0.
- Setup/ZIP .91: el manifest tiene31 archivos presentes; los18 archivos de UI publicados coinciden por SHA-256 con `src/FocusGuard.UI/dist`; los ZIP Chrome/Edge tienen14 entradas cada uno y hashes iguales a `distribution.json`. `build-installer.ps1` cotejó el hash del Desktop y los recursos Host embebidos.

Recibo, Setup y ZIP: `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\build-receipt.json`, `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\FocusGuard-0.1.91-Setup.exe` y `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\FocusGuard-0.1.91-win-x64-menu.zip`.

## Aceptación instalada

El owner instaló Setup .91. Service PID36440 está `Ready` como `NT AUTHORITY\LocalService`; DrillClient después de la instalación y después de la muestra de recursos devuelve `serviceReady=true`, `extensionReady=true`, `sessionState=Inactive`, `sessionId=null`. La NativeMessaging activa proviene de Chrome; Edge real no está verificado.

Los18 archivos de `C:\Program Files\FocusGuard\ui` coinciden por SHA-256 con la publicación .91; la distribución instalada es .91 y los ZIP Chrome/Edge instalados coinciden con sus hashes. `FocusGuard.UI.exe --pilot` abre desde Program Files como usuario normal, PID38160.

Captura WebView2 a1216×799: `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\installed-host-profile-0.1.91-screen.png`. UIA del renderer identifica «Actualizar perfiles» (142×45 px) y «Actualizar estado» (116×39 px) como botones separados, visibles y keyboard-focusable. El recuento del árbol de la página es189 descendientes; no es una prueba Narrator ni una pasada completa de Tab.

La muestra idle60s FG266 conserva sesión `Inactive`; ver `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\focusguard-resources-0.1.91-inactive-profile-host-60s.json`. No se inició ni detuvo una sesión y no se modificaron perfiles/reglas.
