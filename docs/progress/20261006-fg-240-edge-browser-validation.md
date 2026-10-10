# FG-240 — Bloqueo web real con Edge .79 pendiente

## Preparación comprobada

- Setup/ZIP .79 están instalados; el paquete de extensión Edge .79 pasa `test-browser-extension-package.ps1` y su `manifest.json` mantiene el ID esperado.
- El ZIP Edge instalado tiene SHA-256 `928C3D851BB6A4196505EA2C8C8CAC6EFA51F773A89F2013CFF31BDD4EB7FEF2`, igual al hash de distribución del recibo FG237. El staging instalado dice `0.1.79`; su ID `emhdinkjpinjnmldipnejnbeefgonjnj` coincide con el `allowed_origins` de `native-messaging/edge.json`.
- El servicio LocalService responde `Ready`. `FocusGuard.DrillClient status` informó `extensionReady=true` y sesión `Expired`; esto confirma el canal disponible, pero no demuestra que una navegación real esté bloqueada.
- El proceso Native Messaging actual (PID26456) fue lanzado por `cmd.exe` con pipes `chrome.nativeMessaging.in/out`; por tanto, `extensionReady=true` corresponde al cliente Chrome activo, no acredita que Edge tenga la extensión cargada.
- Captura de la UI Host .79 abierta durante esta revisión: `D:\TEMP\focusguard-fg166-validation\artifacts\fg240\host-current.png`.

## Intento de validación y límite

El proceso Edge aislado de FG239 sigue vivo en `D:\TEMP\focusguard-fg166-validation\artifacts\fg239\edge-profile`; lo dejé intacto. La revisión automática rechazó el nuevo intento de abrir otro Edge aislado, antes de crear el perfil FG240, con `blocked by policy`. No se cargó la extensión ni se navegó a ningún sitio en esta comprobación.

Una consulta anterior a `msedge.exe --version` respondió `Opening in existing browser session`, por lo que pudo enfocar una ventana Edge existente. No se pasó una URL ni se realizó navegación; no volví a invocar Edge de esa forma.

El skill `computer-use` requiere la CLI Orca, pero `orca` no está instalada en esta sesión. No intenté evitar el rechazo con otra vía de lanzamiento. La coincidencia de hashes, ID, registro y `extensionReady` no reemplaza el ensayo de navegador real.

## Siguiente paso

Cuando haya una vía aprobada de control del navegador, completar en un perfil Edge de prueba cuyo aislamiento sea visible/verificable: cargar la extensión .79 instalada, confirmar el Native Messaging host autorizado, aplicar `Block bing.com` en un perfil temporal FocusGuard, iniciar un timer finito con bloqueo activado, observar la navegación redirigida a la página local de bloqueo y dejar expirar. No usar el perfil personal como sujeto del ensayo.
