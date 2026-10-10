# FG-261 — Denegación remota del pipe de control instalado

## Plan

Probar el camino permitido de diagnóstico y un intento remoto no autorizado contra el servicio instalado .86 bajo LocalService. No enviar un frame ni una orden de sesión; comprobar el estado del servicio después.

## Resultado

- Antes de la prueba, `FocusGuard.DrillClient.exe status` devolvió `serviceReady=true`, `extensionReady=true` y la sesión `Expired` con SessionId `4b203b6b-f6c4-4554-a6fb-4f8153945330`.
- Un cliente sin elevación intentó abrir `localhost\pipe\FocusGuard.Control.v2` con `NamedPipeClientStream`. Windows rechazó `Connect` con `System.UnauthorizedAccessException`, inner HRESULT `-2147024891` (`0x80070005`, acceso denegado). No se escribió ningún frame.
- Una sonda self-contained desde `D:\TEMP\focusguard-fg166-validation\artifacts\fg262\out\UnauthorizedStatusProbe.exe` conectó al pipe local y envió un `StatusRequest` válido. El servicio cerró sin respuesta. El ejecutable está fuera de `C:\Program Files\FocusGuard`; el flujo de `AuthenticatedIpcServer.AuthorizeControl` llama a `ProtectedIpcDeployment.Observe`, que rechaza imágenes cuyo directorio no es el install root antes de despachar `StatusRequest`. El resultado, ruta e imagen hash están en `D:\TEMP\focusguard-fg166-validation\artifacts\fg262\local-untrusted-status-probe.json`.
- Después, DrillClient volvió a recibir `ServiceReady` y la misma SessionId `Expired`. SCM Service continúa Running como `NT AUTHORITY\LocalService`, PID30976; proceso creado el 6-oct a las18:28:13 local.
- Consulta System de 24 horas no muestra 7034/7036 posteriores al evento del 6-oct a las18:28:01.

Evidencia repetida/capturada: `D:\TEMP\focusguard-fg166-validation\artifacts\fg261\remote-pipe-probe-repeat.json` y `D:\TEMP\focusguard-fg166-validation\artifacts\fg262\local-untrusted-status-probe.json`.

## Alcance

Esto cubre el cliente de diagnóstico permitido, el rechazo remoto por ACL y el rechazo de una imagen local fuera del install root antes de ejecutar `StatusRequest`. No prueba un rol incorrecto desde un binario cuya ruta sí está autorizada, el rol WebViewHost con su manifiesto exacto ni rollback de instalador. El log de rechazo en ProgramData no es legible por este token; el rechazo local se infiere del EOF, el estado intacto y el flujo de código, no de ese JSON. No demuestra la causa del 7034 de las18:28. FG-235 y el rollback owner-present permanecen abiertos.

La prueba no instaló, detuvo ni reinició el servicio y no cambió datos de sesión. El token de Codex no está elevado.

## Siguiente paso

Con el owner y elevación, completar los casos permitidos/rechazados por rol local y el rollback de instalación .89, manteniendo una sesión expirada y capturando transcript/logs legibles.
