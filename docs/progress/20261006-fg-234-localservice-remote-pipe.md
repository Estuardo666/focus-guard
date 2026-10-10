# FG-234 — Rechazo de pipe remoto con el servicio instalado

## Procedimiento

Se verificó el servicio FocusGuard instalado con `sc.exe qc/queryex FocusGuard`: imagen `C:\Program Files\FocusGuard\FocusGuard.Service.exe`, cuenta `NT AUTHORITY\LocalService`, estado `RUNNING`, PID44900. La enumeración local confirmó que `FocusGuard.Control.v2` existía.

Desde PowerShell del mismo usuario se creó `NamedPipeClientStream("localhost", "FocusGuard.Control.v2", InOut, Asynchronous)` y se intentó sólo `Connect`, con timeout. El cliente recibió `System.UnauthorizedAccessException`, HResult `-2147024891`, código Win32 5, mensaje `Access to the path is denied`. No se escribió ningún byte/frame ni se pidió un comando de sesión.

En la comprobación inmediata el servicio se reportó `Running`; no había Host React abierto y no se inició ni detuvo sesión alguna durante la sonda. Después se encontró SCM7034 a las 14:35:38 y el proceso actual cambió de PID44900 a4632 a las 14:35:44. El rechazo remoto sí está observado, pero la estabilidad posterior y su causa no están probadas.

## Alcance pendiente

La prueba usa la identidad actual a través de la ruta SMB/localhost; no equivale a un usuario interactivo local con otro SID. Esa prueba cross-user aún requiere una segunda identidad local y permanece abierta en Fase4. La ACL incluye deny del Network SID y el endpoint real devolvió acceso denegado.
