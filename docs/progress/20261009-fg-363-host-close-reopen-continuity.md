# FG-363 — Continuidad del Host al cerrar y reabrir

## Plan

1. Confirmar Service .123 en LocalService, estado inactivo, ExtensionReady y ausencia de FocusGuard.TestTarget.
2. Iniciar desde el Host instalado .123 una sesión normal de 1 min en gpt test con bloqueo de aplicación, Strict off y sin sitios bloqueados.
3. Esperar a que el servicio informe Active y enforcementConnected=true; guardar SessionId y EndUtc.
4. Cerrar solo la ventana del Host y reabrir C:\Program Files\FocusGuard\FocusGuard.UI.exe --pilot como el propietario.
5. Verificar que el Host recupera el mismo SessionId y EndUtc, los canales siguen Ready y la sesión no se interrumpió.
6. Dejar expirar el timer naturalmente, confirmar Expired y que TestTarget no se ejecutó.
7. Restaurar desktop-settings.json y el formulario del Host a los valores previos; conservar el registro local de la prueba.

## Seguridad y recuperación

La sesión es normal, breve y no estricta. El perfil existente gpt test solo bloquea FocusGuard.TestTarget.exe y permite focusguard.invalid. No se iniciará dicho target ni se navegará a sitios bloqueados. Si el preflight indica una sesión activa, reglas inesperadas o estado distinto, no se iniciará la prueba.

## Resultado instalado .123

- Preflight Diagnostic: ServiceReady=true, ExtensionReady=true, SessionState=Expired, Strict=false; ProcessAgent PID 5336 presente; FocusGuard.TestTarget ausente.
- La revisión real del Host confirmó gpt test, SimpleTimer de 1 min, enforcement activo, 1 regla de aplicación bloqueada, 0 sitios bloqueados y EndUtc 11:47:48 local.
- Antes de cerrar Host PID 25144, Diagnostic vio Active, SessionId ed3f69ed-5e08-4264-b65a-3c0542d19e31, EndUtc 2026-10-09T16:47:48Z, remaining 49 s, extensionReady=true, enforcementConnected=true.
- Con la ventana cerrada, el servicio siguió Active con el mismo SessionId y EndUtc (remaining 23 s); ServiceReady/ExtensionReady/enforcementConnected permanecieron true.
- Reabrí C:\Program Files\FocusGuard\FocusGuard.UI.exe --pilot como propietario (Host PID 36676). El árbol del Host mostró la sesión en curso, perfil gpt test, Servicio conectado, Extensión disponible y Bloqueo confirmado. Diagnostic volvió a leer el mismo SessionId/EndUtc (remaining 4 s).
- La sesión terminó naturalmente como Expired, remaining 0, Strict=false, ServiceReady/ExtensionReady=true y enforcementConnected=false. No se ejecutó FocusGuard.TestTarget.
- Restauré desktop-settings.json al estado anterior tomando como referencia artifacts/fg362-output/preferences-restore.json; el formulario quedó en perfil 2, ciclo Enfoque profundo, 3 min y bloqueo activado. El journal conserva la sesión de prueba.

Evidencia estructurada: artifacts/fg363-output/host-close-reopen-continuity.json. Capturas: fg363-review-one-minute.png, fg363-host-reopened-active.png, fg363-host-expired-before-preferences-restore.png y fg363-host-form-restored.png.

## Comandos, archivos e implicaciones

- Lecturas ejecutadas: C:\Program Files\FocusGuard\FocusGuard.DrillClient.exe status antes, durante y después; Get-Service/Process para comprobar LocalService, Host, Agent y ausencia de TestTarget.
- UI ejecutada: revisión y confirmación normal desde el Host .123; cierre de su ventana; Start-Process de C:\Program Files\FocusGuard\FocusGuard.UI.exe --pilot desde Program Files; lectura Orca del Host reabierto.
- Archivos de evidencia creados en artifacts/fg363-output: continuidad JSON, cuatro capturas y restauración de preferencias. Documentación actualizada en TASKS.md y FOCUSGUARD_UI_MIGRATION_PLAN.md. No cambió código de producto ni se añadieron pruebas automatizadas en este hito.
- UX: el Host instalado recuperó la sesión y mostró sus controles de sesión, cuenta regresiva y canales confirmados después de reabrirse.
- Seguridad: perfil acotado, regla de bloqueo de una app de prueba, cero sitios bloqueados y Strict apagado. No se ejecutó el objetivo bloqueado; el registro local de la sesión aceptada se conserva.

## Alcance

FG363 verifica continuidad de sesión tras cerrar/reabrir el Host instalado .123. No acepta .128, no prueba reconexión tras caída del Agent ni cierra la matriz restante de Fase 4. La instalación del candidato .128 y la aceptación de sus cambios siguen pendientes.
