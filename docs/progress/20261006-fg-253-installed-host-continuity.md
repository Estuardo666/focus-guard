# FG-253 — Continuidad del Host .86 con el servicio instalado

## Comprobación instalada
Los hashes SHA-256 de Service, Desktop, UI Host, ProcessAgent, NativeMessaging y DrillClient en Program Files coinciden con los componentes del paquete .86. Service continúa Running como NT AUTHORITY\LocalService, PID30976. El Host .86 se abrió desde Program Files como usuario normal y presentó estado real del servicio/extensión y una sesión normal activa.

La sesión ya estaba activa antes de la prueba; no envié Activate ni Stop. Se registró SessionId 4b203b6b-f6c4-4554-a6fb-4f8153945330, strict false, enforcementConnected true y 1426s restantes. Cerré solo el formulario con CloseMainWindow. La consulta inmediata devolvió el mismo SessionId/Active/1425s. Reabrí FocusGuard.UI.exe con --pilot; el proceso nuevo mostró el mismo SessionId/Active, serviceReady=true, extensionReady=true y enforcementConnected=true con1424s. MainForm.OnFormClosing dispone el WebView/puente y no envía una orden Stop. No se forzó la terminación del proceso ni se cambió la sesión.

## Revisión visual disponible
Captura del Host instalado después de reabrir, 1216×799 en tema oscuro: D:\TEMP\focusguard-fg166-validation\artifacts\fg253\host-0.1.86-installed-active-session.png. Muestra el dashboard React y los datos reales de la sesión. El contrato visual sigue mostrando una etiqueta de piloto y una sesión de perfil guardado; la pantalla no demuestra bloqueo de Edge ni de una aplicación concreta.

## Gates pendientes
No se revisaron aún tema claro, forced-colors, teclado físico ni ventanas de320/700/1409px. Setup package-self-test y smoke gráfico tampoco se ejecutaron. La continuidad de estado Host↔Service está probada para esta sesión normal; los clientes permitidos/rechazados y rollback bajo LocalService siguen pendientes en Fase4. Edge real queda en FG240 porque extensionReady no identifica el navegador ni prueba navegación bloqueada. El nuevo evento SCM7034 de las18:28 coincide en el tiempo con LastWriteTime18:26:17 de los binarios .86, pero no hay 7036, WER/.NET ni transcript correlacionados; sigue sin causa demostrada. Codex no puede leer el log de ProgramData por ACL.
## Versión de los paquetes de navegador
Program Files conserva distribution.json y ZIPs Chrome/Edge .85; sus hashes coinciden con ese manifest. Comparé cada ZIP instalado .85 con el ZIP .86 preparado: tienen14 entradas y solo cambia manifest.json; los archivos de la extensión son idénticos. Las claves Native Messaging de HKCU para Chrome y Edge existen y usan el mismo ID, pero no abrí Edge ni vi una conexión Edge. extensionReady=true y el NativeMessaging activo no son evidencia de Edge. Los archivos .85 son staging sin firma de la tienda; la publicación .86 requiere las cuentas editoras (FG-113).
