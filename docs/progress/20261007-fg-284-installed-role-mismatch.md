# FG-284 — Denegar un Diagnostic autorizado en el pipe del Agent

## Preparación

`FocusGuard.DrillClient.exe` ya es un ejecutable autorizado por el manifiesto instalado con rol `Diagnostic`; su comando `status` usa el endpoint Control y había recibido `serviceReady=true`. Añadí `probe-agent-role-denied`, que envía un heartbeat ProcessAgent válido al pipe `FocusGuard.ProcessAgent.v1` mediante el transporte autenticado, pero desde esa identidad Diagnostic. El mensaje no contiene auditoría, acknowledgement, policy ni datos de sesión.

El Service procesa el framing y valida el tipo antes de aplicar `AuthorizeAgent`; la autorización por identidad nativa rechaza el rol antes de `relay.RecordHeartbeat`. La sonda no activa, detiene ni cambia una sesión.

## Instalación y resultado

Se instaló Setup .97 mediante el flujo elevado del instalador. 18/18 archivos UI y 7/7 ejecutables principales coinciden por SHA-256 con el publish. Service PID44716 corre como LocalService. Antes, el DrillClient Status reportó Ready/extensionReady/Inactive y SessionId nulo; Agent PID24952 estaba activo.

Desde `C:\Program Files\FocusGuard\FocusGuard.DrillClient.exe`, `probe-agent-role-denied` produjo exactamente `DENIED_EOF` y salió0. Después, Service y Agent conservaron los mismos PID; un StatusRequest volvió a devolver Ready/extensionReady/Inactive y SessionId nulo. Esto demuestra que una ruta/hash autorizados no convierten el rol Diagnostic en ProcessAgent. Los cuatro smokes del Host instalado .97 también terminaron0.

Recibo: `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/installed-role-mismatch-0.1.97.json`. El build y los hashes están en `build-receipt-0.1.97.json`.

## Pendiente

Queda ensayar rollback owner-present con fallo controlado y correlacionar SCM7034. La UI React .97 mantiene las mejoras visuales de .96; no se cambió el perfil ni se inició/terminó sesión.
