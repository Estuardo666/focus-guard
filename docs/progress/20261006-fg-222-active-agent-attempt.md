# FG-222 — Handshake ProcessAgent, bloqueo app y continuidad del Host

## Plan

- Comprobar paquete/servicio 0.1.71 y sesión inactiva antes de un timer normal de prueba.
- Usar `gpt test` sólo tras confirmar sus reglas: Block `FocusGuard.TestTarget.exe`, Allow `focusguard.invalid`.
- Verificar Agent, bloqueo app y continuidad de sessionId al cerrar/reabrir el Host.
- Mantener una protección de automatización: cualquier entrada UI requiere que el HWND/PID/título de FocusGuard coincidan con foreground.

## Resultado del primer intento

El Host `FocusGuard.UI.exe --pilot` activó el timer normal de 1 minuto con enforcement. El servicio estuvo Ready bajo `NT AUTHORITY\LocalService`, pero no corría `FocusGuard.Desktop.exe` ni ProcessAgent; `enforcementConnected=false`. El timer expiró sin lanzar el target. HKCU Run contenía los comandos de inicio para ambos. Arrancar `FocusGuard.Desktop.exe --minimized` como usuario normal inició Desktop PID30420 y ProcessAgent PID44392. No se cambió el perfil ni el horario.

## Retest instalado aprobado

Con Desktop/Agent presentes, la revisión normal de `gpt test` mostró una aplicación Block, cero sitios Block, modo no estricto y 1 minuto. El servicio entró a `Active` con `enforcementConnected=true`, `serviceReady=true`, `extensionReady=true`. Se lanzó el `FocusGuard.TestTarget.exe` cooperativo desde el build E con lifetime configurado de 120 segundos; desapareció en menos de 4 segundos mientras el timer conservaba 19 segundos. Eso confirma el cierre por regla antes del límite natural.

El Host PID38136 se cerró normalmente mientras la sesión estaba activa. El status tras cerrar y tras reabrir el Host como PID30724 mantuvo sessionId `ea46d89f-1224-42ce-8832-8ae6694e8900`, mismo endUtc y `enforcementConnected=true`. La sesión llegó a `Expired`; no se modificaron perfiles ni horarios. Quedaron dos registros de sesión de prueba en Actividad (primer intento sin Agent y retest aprobado).

## Verificación

- `dotnet test tests/FocusGuard.Desktop.Tests/FocusGuard.Desktop.Tests.csproj -c Release --no-build --filter FullyQualifiedName~ProcessAgent`: 6/6.
- Binario ProcessAgent instalado SHA-256 coincide con paquete 0.1.71.
- Servicio final `Running/LocalService`, `serviceReady=true`, `extensionReady=true`, `sessionState=Expired`; Desktop PID30420 y Agent PID44392 siguen presentes.
- `gpt test` tiene un Allow para `focusguard.invalid`, así que no se bloqueó ningún sitio. El bloqueo web y el handshake NativeMessaging durante una navegación no se prueban aquí.
- Perfiles y horarios siguen iguales. No hubo un test strict.

## Seguridad, UX y límites

La sesión tenía una única aplicación objetivo de prueba y ningún dominio Block; no afectó aplicaciones o sitios de uso normal. Durante un intento previo un click sin guard de foreground alcanzó otra ventana; no se escribió ni envió texto y se borró la captura accidental. Todos los inputs de este retest se enviaron sólo tras verificar PID/HWND/título de FocusGuard. La captura del estado activo mostró `Confirmación de bloqueo: Confirmada`.

No se accedió a los logs protegidos bajo ProgramData porque el proceso actual es de usuario normal; no se elevó ni automatizó UAC. El informe de diagnóstico está en `D:\TEMP\focusguard-fg166-validation\artifacts\fg222\phase4-installed-acceptance\fg222-agent-session-attempt.json`.

## Pendiente

Crear un perfil/target web de prueba Block aislado y verificar el navegador con la extensión instalada. Completar DPI, Windows 10/11, teclado/accesibilidad y el resto de Fases 8–10.
