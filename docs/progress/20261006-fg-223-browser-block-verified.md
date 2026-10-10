# FG-223 — Bloqueo web real verificado en Edge

## Entorno

- Host instalado FocusGuard.UI0.1.71, iniciado sin elevación con `--pilot`.
- FocusGuard Service `C:\Program Files\FocusGuard\FocusGuard.Service.exe`, `NT AUTHORITY\LocalService`, PID44900, estado `Running`.
- Edge Standard UI en un perfil temporal separado de `Personal`; FocusGuard extension0.1.77 cargada como unpacked desde el paquete .77. Native Messaging ya estaba registrado para el ID oficial `emhdinkjpinjnmldipnejnbeefgonjnj`.
- El default Edge profile seguía con Sync activado y sus extensiones existentes; no se modificó ni se usó para la prueba.

## Prueba

1. Confirmé que el servicio estaba activo y la sesión `Inactive` desde el Host instalado en modo consulta.
2. Creé el perfil FocusGuard temporal `Codex QA Browser 20261006`; reglas de aplicación0 y dos reglas web Block: `focusguard-test.invalid` y `bing.com`.
3. Inicié una sesión normal de1 minuto con el bloqueo activado, sin Strict Mode. El Host mostró servicio `Conectado`, extensión `Disponible` y bloqueo `Confirmada`.
4. En Edge del perfil aislado hice una búsqueda normal en Bing. FocusGuard interceptó la navegación a `https://www.bing.com/search` y la redirigió a `extension://emhdinkjpinjnmldipnejnbeefgonjnj/status.html?...`; la página dijo “www.bing.com will still be there later” con el tiempo restante. Captura: `D:\TEMP\focusguard-fg166-validation\artifacts\fg230\edge-bing-block-result.png`.
5. Detuve la sesión con12 segundos restantes; la UI volvió a `Nueva sesión` y el servicio continuó `Running`.

El primer intento a `.invalid` no produjo navegación directa: Edge interpretó la URL como búsqueda. No se usó como evidencia de bloqueo; la regla real `bing.com` permitió una navegación controlada y comprobable. No se reintentaron los flags Chrome rechazados anteriormente.

## Limpieza y límites

El perfil de prueba y sus reglas se eliminaron desde la UI de FocusGuard. Se retiró la extensión del perfil Edge de QA y se borró ese perfil Edge, que se creó en esta sesión. Verificación final: no existe `%LOCALAPPDATA%\Microsoft\Edge\User Data\Profile 2`; el ID FocusGuard no existe bajo `Default` ni el perfil preexistente `Profile 1`. El perfil `Personal`, sus extensiones sincronizadas y el perfil preexistente permanecieron intactos. Host y Edge de QA se cerraron; el servicio quedó `Running`. El historial conserva las dos sesiones de prueba porque la confirmación de borrar perfil explícitamente no borra historial.

Esto valida el bloqueo web real contra el servicio LocalService instalado .71 y extensión .77. No es todavía la aceptación del Host .77 instalado ni del bloqueo de aplicaciones en este mismo piloto.
