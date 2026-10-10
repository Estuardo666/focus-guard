# FG-368 — Recuperación del Agent ante pérdida de canal .123

## Objetivo

Validar en el Host y el servicio instalados que una pérdida real del proceso ProcessAgent durante una sesión normal no altera la sesión y que el Host lo vuelve a iniciar.

## Precondiciones

- Host .123 abierto en modo piloto; FocusGuard Service Running como LocalService.
- Diagnostic: ServiceReady/ExtensionReady true, estado previo `Expired`, Strict=false.
- ProcessAgent instalado en Program Files, PID 5336. `FocusGuard.TestTarget.exe` no estaba ejecutándose.
- Preferencias locales respaldadas antes de la prueba (SHA-256 `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55`).

## Prueba y resultado

Inicié desde el Host una sesión normal de 1 minuto con `gpt test`, bloqueo activado, un ejecutable de prueba bloqueado y cero sitios bloqueados. Diagnostic confirmó estado Active, `enforcementConnected=true`, `extensionReady=true`, Strict=false y fin `2026-10-09T17:50:28Z`.

Terminé el Agent PID 5336. El Host supervisor lo reinició automáticamente como PID 31992. Durante la reconexión, Diagnostic observó `enforcementConnected=false` y luego `true`; el mismo SessionId `0c695082-5a94-4ca6-8cf6-e0a2fa5d227f` y EndUtc se conservaron. `FocusGuard.TestTarget.exe` no se ejecutó. La sesión llegó a `Expired` de forma natural; ServiceReady/ExtensionReady siguieron true, Strict=false y `enforcementConnected=false` después de expirar.

Cerré y reabrí el Host .123 tras la expiración. Restauré `desktop-settings.json` exactamente a su hash anterior; el Host arrancó de nuevo con el perfil y ciclo anteriores. El duplicado temporal de restauración verificado queda en `D:\TEMP\FocusGuard-FG368-desktop-settings-restore-124742.json`.

## Alcance y pendiente

Confirma pérdida y reconexión real del Agent durante una sesión normal bajo Service LocalService en .123. No valida todavía el mismo escenario tras instalar .128, durante un reboot ni con Strict. No se detuvo el servicio ni se modificaron perfiles o reglas.

Evidencia: `artifacts/fg368-output/installed-agent-channel-loss-recovery.json`.
