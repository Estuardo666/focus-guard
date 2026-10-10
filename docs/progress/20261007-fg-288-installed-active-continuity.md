# FG-288 — Continuidad activa y bloqueo de aplicación en .98

## Plan y alcance

Validar una sesión normal breve desde React instalado, usando el perfil existente gpt test, cerrar/reabrir el Host y observar expiración natural. Sólo bloquear el ejecutable de prueba; conservar las reglas del perfil. La aceptación no cubre navegador, ciclos ni modo estricto.

## Evidencia

La sesión f3aa6759-4deb-45ff-b2b7-8c255c4d95cc inició a21:39:32.4637252Z y terminó a21:41:32.4637252Z. Service reportó Active y enforcementConnected=true. TestTarget PID19136 terminó dentro de3s; Actividad mostró un intento bloqueado. El Host piloto PID4548 dejó de estar abierto y el Host normal PID23064 mostró la sesión aún Active, con el mismo ID y EndUtc y77s restantes. El timer expiró sin Stop. Una consulta posterior confirmó Expired con el mismo ID y fin.

El perfil contenía Block/FileName FocusGuard.TestTarget.exe y Allow focusguard.invalid. No se cambiaron reglas ni se habilitó Strict. Quedaron dos sesiones de prueba (1 y2min) en Actividad.

Recibo: D:/TEMP/focusguard-fg166-validation/artifacts/fg280/installed-active-session-continuity-0.1.98.json. Captura: installed-active-session-host-reopen-0.98.png en la misma carpeta.

## Limitaciones

El primer timer de1min expiró antes de completar la reapertura y no demuestra continuidad durante Active. El segundo sí aporta esa evidencia. El resumen indicó consentimiento de bloqueo desconocido para Repeat; esa discrepancia necesita investigación antes de aceptar repetición de sesiones nuevas. El comando Diagnostic activity cerró el pipe; la lectura Today/resumen desde React sí funcionó.

## Siguiente gate

Edge no mostró FocusGuard en su lista de extensiones. El intento de cargar staging .98 quedó en el selector de carpetas Windows/TotemHub y se canceló sin confirmar la instalación. No se reclama bloqueo web. Resolver la carga temporal y validar una navegación aislada; después investigar consentimiento de Repeat y la consulta Diagnostic activity.
