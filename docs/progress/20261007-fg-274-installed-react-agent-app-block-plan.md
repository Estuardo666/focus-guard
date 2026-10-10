# FG-274 — Aceptación instalada de Agent desde React y bloqueo de app

## Estado inicial

Setup .92 terminó de instalarse. El servicio está `Running` como `LocalService` PID34536; `DrillClient status` informa `Inactive`, serviceReady y extensionReady, sin enforcement. Los archivos UI instalados coinciden con el publish .92. `FocusGuard.ProcessAgent` existe pero su padre es `FocusGuard.Desktop` PID37168, iniciado por el instalador; eso no prueba que React pueda iniciar el Agent.

## Ensayo owner-present

1. Mantener la sesión del servicio en `Inactive` y cerrar Avalonia por su ruta normal. Si el Agent queda vivo, terminar sólo el proceso Agent identificado, sin sesión activa, para crear una condición fría.
2. Abrir `C:\Program Files\FocusGuard\FocusGuard.UI.exe --pilot` sin elevación; confirmar que el Host crea un Agent de usuario y el servicio sigue `Inactive`.
3. Inspeccionar el perfil temporal `FG App Block Test`; revisar que su única regla sea `Block: FocusGuard.TestTarget.exe`.
4. Revisar una sesión normal de1min, Strict Off, enforcement On y0 reglas web. Confirmar sólo si el servicio muestra `Active` y `enforcementConnected=true`.
5. Iniciar el ejecutable cooperativo con `--lifetime 30`; observar que el Service termina ese PID por la regla de bloqueo antes del lifetime. No lanzar Edge ni usar otras aplicaciones.
6. No enviar Stop: dejar expirar el timer. Verificar `Expired`, `remainingSeconds=0`, y luego eliminar el perfil temporal tras cerrar la sesión.

## Criterio de parada

Si la revisión no muestra la única regla temporal o el servicio no llega a `Active` con enforcement conectado, no lanzar TestTarget. No activar Strict. La lectura de logs en ProgramData permanece restringida por ACL y no se cambiará.

## Resultado instalado .92

- Antes del test, Service .92 estaba `Inactive` como `LocalService` PID34536. Cerré Classic Desktop PID37168 y el Agent PID25220 con enforcement inactivo, después abrí `FocusGuard.UI.exe --pilot` sin elevación como PID26484. Apareció ProcessAgent PID35428 con padre `FocusGuard.UI.exe`; el Service siguió `Inactive` hasta activar la sesión.
- En Profiles confirmé `FG App Block Test`: una regla de app `Block: FocusGuard.TestTarget.exe`, cero reglas web. La revisión normal de2min tuvo Strict Off, Enforcement On y fin exacto14:21:32Z.
- SessionId `1513d202-71ec-47fc-81a8-5eb8af8d8582` llegó a `Active`, remaining101, `enforcementConnected=true`. Se lanzó TestTarget con lifetime propio50s; durante la sesión seguía ausente con remaining75. Su PID no quedó capturado por el wrapper, pero el resumen de esa SessionId muestra `Intentos bloqueados: 1`, y el perfil sólo tenía esa regla de app.
- Cerré Host PID26484 y reabrí PID20452. Service conservó el mismo SessionId `Active`, enforcement=true, y el mismo Agent PID35428. Sin Stop, expiró a `Expired`/0; TestTarget no estaba corriendo. Luego el perfil temporal se eliminó y la lista confirmó que ya no existe.

Evidencia estructurada: `D:\TEMP\focusguard-fg166-validation\artifacts\fg274\installed-react-agent-app-block-0.1.92.json`. El bloqueo real de navegación Edge no se probó.
