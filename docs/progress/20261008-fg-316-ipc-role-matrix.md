# FG-316 — Manifiesto y matriz de roles IPC instalada

Fecha: 2026-10-08

Estado: auditoría de la instalación .102 completada; no cierra la aceptación funcional de Fase4.

## Evidencia instalada

Se leyó `C:\Program Files\FocusGuard\ipc-policy.json` y se compararon sus hashes contra los archivos instalados. El manifiesto schema1 contiene el SID del propietario, la raíz `C:\Program Files\FocusGuard` y cinco clientes; la entrada del Service también coincide. Todos los hashes de ejecutable y DLL administrada coinciden:

| Rol | Ejecutable | DLL | EXE/DLL según manifiesto |
| --- | --- | --- | --- |
| Desktop | `FocusGuard.Desktop.exe` | `FocusGuard.Desktop.dll` | Coinciden |
| ProcessAgent | `FocusGuard.ProcessAgent.exe` | `FocusGuard.ProcessAgent.dll` | Coinciden |
| NativeMessaging | `FocusGuard.NativeMessaging.exe` | `FocusGuard.NativeMessaging.dll` | Coinciden |
| WebViewHost | `FocusGuard.UI.exe` | `FocusGuard.UI.dll` | Coinciden |
| Diagnostic | `FocusGuard.DrillClient.exe` | `FocusGuard.DrillClient.dll` | Coinciden |
| Service | `FocusGuard.Service.exe` | `FocusGuard.Service.dll` | Coinciden |

La ACL del manifiesto está protegida: SYSTEM y Administradores tienen FullControl; LocalService y Usuarios tienen ReadAndExecute/Synchronize. `FocusGuard` permaneció Running como LocalService, PID25096. El DrillClient .102 volvió a confirmar `serviceReady=true`, `extensionReady=true` y `Expired/0`.

El manifiesto persiste `ownerUserSid`; no fija un Logon SID ni un SessionId. `AuthenticatedIpcServer` obtiene ambos del peer real del named pipe y vincula la primera conexión autorizada durante la vida del servicio, en vez de confiar en valores del mensaje.

## Matriz definida por código y evidencia viva

- `Control`: Desktop, WebViewHost y Diagnostic; el allowlist actual incluye todas las solicitudes de control, incluidas activación y parada.
- `ProcessAgent`: sólo su endpoint y los tipos `heartbeat`/`audit`.
- `Browser`: sólo NativeMessaging y los tipos `status`/`heartbeat`/`audit`/`capture`.

FG-311 aporta evidencia viva para un StatusRequest Diagnostic permitido y un heartbeat Diagnostic al pipe ProcessAgent rechazado con `DENIED_EOF`. La prueba unitaria `IpcAuthorizationPolicyTests` cubre los cruces entre roles, pero esta pasada no probó los clientes ProcessAgent/NativeMessaging aceptados, ni otro usuario, imagen manipulada, servidor falso o rollback.

El rol Diagnostic no es de sólo lectura en el código: su política le permite las solicitudes mutadoras de Control y `FocusGuard.DrillClient.exe` incluye `activate-strict`. No ejecuté ese comando. El alcance es intencional en los tests actuales como cliente de validación, pero la fase queda abierta hasta aceptar explícitamente esta capacidad y probar el comportamiento instalado de los otros roles.

Recibo sin SID ni hashes: `artifacts/fg315-output/fg316-installed-ipc-role-matrix.json`. Paquete instalado aún .102; rollback de .102 y validación de Activity .104 siguen pendientes.

FG-317 agregó una ruta read-only para preparar el rollback con un paquete explícito. Su preflight .104 pasó sin invocar Setup; la restauración real continúa pendiente.

### Addendum FG-319

El análisis posterior mostró que el rol Diagnostic de la instalación .102 también podía enviar mutaciones Control, incluido el `activate-strict` del DrillClient. El código .106 reduce ese rol a Status/Activity y transforma el reboot drill para usar la revisión Strict de la UI y esperar canales reales. La instalación vigente .102 conserva la política anterior hasta aceptar .106.
