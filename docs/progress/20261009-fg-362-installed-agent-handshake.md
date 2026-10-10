# FG-362 — Handshake de ProcessAgent con política activa

## Objetivo

Cerrar una parte concreta de Fase 4 con el servicio LocalService instalado: confirmar que un cliente Diagnostic autorizado puede leer status y que ProcessAgent tiene conexión durante una política normal activa.

## Preflight comprobado

- Diagnostic instalado .123: status exit 0, ServiceReady y ExtensionReady true, SessionState Inactive, SessionId null, Strict false y enforcementConnected false.
- ProcessAgent instalado presente en la sesión del propietario.
- FocusGuard.TestTarget.exe no está abierto.

## Plan y límites

1. Desde el Host instalado, revisar e iniciar una sesión normal de 1 minuto con enforcement activado usando el perfil existente gpt test.
2. No modificar el perfil, sus reglas ni preferencias; no abrir FocusGuard.TestTarget.exe ni sitios bloqueados.
3. Consultar Diagnostic status durante la sesión y verificar enforcementConnected=true y extensionReady=true.
4. Dejar que el timer expire solo; confirmar SessionState=Expired, Strict=false, sin SessionId activa y canales listos.
5. Si el servicio o el Host reporta un estado distinto, no activar strict ni ejecutar recuperación; detener la prueba y documentar el fallo.

La sesión es breve, normal y no estricta. Se prueba la instalación .123; no se presenta como aceptación del candidato .128.

## Resultado

Diagnostic status inicial: exit 0; ServiceReady=true; ExtensionReady=true; SessionState=Inactive; SessionId=null; Strict=false; enforcementConnected=false. ProcessAgent PID 5336 estaba presente y FocusGuard.TestTarget.exe no estaba abierto.

El Host instalado .123 revisó una sesión normal de 1 minuto con el perfil gpt test. La revisión confirmó 1 aplicación bloqueada, 0 sitios bloqueados y fin exacto 11:30:05; Strict permaneció apagado. La base de perfiles consultada en modo read-only confirma que gpt test bloquea FocusGuard.TestTarget.exe y permite focusguard.invalid. Inicié desde el Host y no ejecuté el target.

Siete consultas Diagnostic durante el minuto confirmaron SessionState=Active, ExtensionReady=true y enforcementConnected=true, con el mismo SessionId 21944eeb-108c-4fb4-a02a-67f9a049bf5b y tiempo restante descendente. La última consulta observó Expired, remainingSeconds=0, Strict=false, ExtensionReady=true y enforcementConnected=false. FocusGuard.TestTarget.exe no se ejecutó. Evidencia completa: artifacts/fg362-output/installed-process-agent-handshake-0.1.123.json.

La sesión expiró por su propio fin programado; no activé Strict ni recovery. FocusGuard guardó LastProfileId, duración y modo al aceptar la sesión; restauré desktop-settings.json usando la intención aceptada previa 6b94add5-d571-4d49-badd-24737e86113d como fuente (perfil 2, 3 min, FocusCycle, preset null), preservando las demás preferencias. El formulario visible quedó en perfil 2, ciclo Enfoque profundo, bloqueo activado según enforcementEnabled original y Strict apagado. El único dato de prueba que se conserva es el registro de la sesión normal expirada. El servicio LocalService sigue Running.

El registro de intención de FG362 permanece en session-journal.json como evidencia de la sesión real; no se borró del historial.

Capturas del paso de revisión y del formulario restaurado después de la expiración: artifacts/fg362-output/fg362-installed-host-review-1min-gpt-test.png y artifacts/fg362-output/fg362-installed-host-expired-restored.png.
Evidencia de restauración de preferencias: artifacts/fg362-output/preferences-restore.json.

## Alcance restante

FG362 confirma el handshake Agent durante una política normal activa en la instalación .123. No cierra la matriz de roles, el ACK Native Messaging, Edge, el reboot/recovery con Strict, ni la aceptación del candidato .128. Avalonia permanece disponible.
