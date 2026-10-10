# FG-331 — Aceptación instalada de FocusGuard 0.1.112

**Fecha:** 2026-10-08  
**Fase:** 4/5/6/7/9  
**Estado:** Instalación y comprobaciones seguras completadas; quedan pruebas interactivas y de rollback.

## Evidencia de instalación y seguridad

- El proceso `FocusGuard-0.1.112-Setup` ya no existe. El servicio `FocusGuard` está `Running` como `NT AUTHORITY\LocalService`, PID 760.
- La extensión instalada declara versión `0.1.112`. Los hashes SHA-256 de los ejecutables y DLL administradas de `Service`, `Desktop`, `ProcessAgent`, `NativeMessaging`, `WebViewHost` y `Diagnostic` coinciden con `ipc-policy.json`; ese manifiesto tiene ACL protegida y su `ownerUserSid` coincide con el SID del usuario activo. Los seis binarios principales también coinciden con el paquete candidato .112. Los 18 assets de `ui/` instalados coinciden con los 18 assets del candidato.
- `FocusGuard.Desktop` (PID 19952), `FocusGuard.ProcessAgent` (10688), `FocusGuard.NativeMessaging` (30040) y `FocusGuard.UI` (25248) pertenecen a `DESKTOP-9KQ9TRG\Stuart`, están en la sesión 1, no están elevados y tienen integridad medium (RID 8192). Se consultaron los tokens con el mismo helper de solo lectura incluido en `install-focusguard.ps1`.

## Pruebas instaladas

- El `FocusGuard.DrillClient.exe status` autenticado termina con exit 0: `serviceReady=true`, `extensionReady=true`, sesión `Inactive`, sin SessionId y `strictMode=false`.
- `activity` termina con exit 0 y devuelve 198 registros en 55,496 bytes, por debajo del límite IPC de 64 KiB.
- `probe-agent-role-denied` termina con exit 0 y `DENIED_EOF`: Diagnostic no puede enviar heartbeat al pipe de ProcessAgent. Los comandos Diagnostic `activate-strict 60` y `acknowledge` salen con exit 1 y el rechazo explícito del cliente, sin abrir el pipe.
- Los cinco smokes ejecutados desde el Host instalado (`--smoke`, `--smoke-runtime-missing`, `--smoke-renderer-failure`, `--smoke-renderer-crash`, `--smoke-window-dpi`) terminan con exit 0.
- FocusGuard UI se abrió desde la instalación como usuario normal. La ventana `FocusGuard` responde; el proceso Host está en medium integrity y tiene un proceso hijo WebView2. El estado del servicio permaneció `Inactive`; no se inició ni activó una sesión.

## Pruebas de rol añadidas

- El `FocusGuard.Desktop.exe --service-health-check` instalado termina con exit 0 y registra `Stage=Ready`; el servicio permaneció Running como LocalService.
- Se lanzó `FocusGuard.NativeMessaging.exe` con el origin Chrome permitido y una consulta `status` correctamente enmarcada. Respondió `healthStatus`, correlation ID coincidente, `serviceReady=true`, `extensionReady=true`, `sessionState=Inactive`; exit 0.
- Un cliente PowerShell no autenticado intentó abrir `localhost\pipe\FocusGuard.Control.v2`; Windows lo rechazó con `UnauthorizedAccessException`/`0x80070005`. No se envió ningún frame. Diagnostic status antes y después siguió `Inactive`, `serviceReady=true`, `strictMode=false`.
- Un `pwsh.exe` del mismo usuario, pero fuera del install root, conectó al pipe local y envió un `StatusRequest` válido; el Service cerró sin respuesta (`EOF`). Diagnostic status antes y después permaneció `Inactive`, `serviceReady=true`, `strictMode=false`; no se cambió sesión ni perfil.
- Tras ambas sondas, Diagnostic volvió a confirmar el servicio `Running`/LocalService, `Inactive`, sin SessionId, Strict desactivado y `enforcementConnected=false`.

## Pendiente

Orca continúa devolviendo `runtime_access_denied` (EPERM), por lo que no hay captura visual ni lectura de Narrator. Siguen pendientes la inspección de las tarjetas durante una sesión activa, bloqueo de aplicación y de Edge con .112, continuidad al cerrar/reabrir, matriz completa de roles, reboot/recovery, cambio real de monitor/DPI, Windows 11, forced-colors, teclado de diálogos/compacto y pruebas de rendimiento activo. El rollback de .112 quedó verificado en FG333.
