# FG-270 — Supervisión de ProcessAgent desde el Host React

## Hallazgo instalado

La instalación .91 y el servicio `FocusGuard` bajo `LocalService` estaban listos; la sesión activa anterior figuraba `Expired`. En la pestaña Perfiles confirmé que el perfil temporal `FG App Block Test` contiene sólo `Block: FocusGuard.TestTarget.exe`. La revisión de sesión mostró ese perfil, una regla de aplicación, cero reglas de sitio, timer de un minuto, bloqueo activado, Strict apagado y hora exacta de fin. Tras `Confirmar e iniciar`, el servicio aceptó la sesión `d25fc16c-94a9-4412-a5c5-8309101e06d2`, pero permaneció en `Activating`, `enforcementConnected=false`; no había proceso `FocusGuard.ProcessAgent`. La sesión llegó a `Expired` automáticamente. No lancé el TestTarget.

La lectura propietaria de `health-check.json` seguía indicando `Ready`. La cuenta de Codex no pudo leer `C:\ProgramData\FocusGuard\diagnostics\service-failures.jsonl` por ACL. No cambié permisos.

## Causa y cambio

`FocusGuard.Desktop.Program` aseguraba el inicio del agente y `App` mantenía `ProcessAgentSupervisor`; `FocusGuard.Host.Windows.Program` no lo hacía. En una ejecución sólo React, el servicio aceptaba el timer, pero carecía del cliente de procesos que debe reconocer la política.

El Host ahora crea `ProcessAgentSupervisorLifetime` antes de abrir la UI en ejecuciones normales. El alcance reintenta mientras vive el Host, cancela y espera al supervisor al cerrar la ventana y no mata al proceso Agent independiente ni interrumpe una sesión. Los escenarios `--smoke` no arrancan un companion instalado. Se añadieron prueba automatizada de arranque inmediato y cierre idempotente, y se incrementó la versión de paquete a .92.

## Validación

- `FocusGuard.Host.Windows.Tests`: 154/154; `FocusGuard.Desktop.Tests`: 189/189.
- Solución .NET completa: 875/875 en serie. Una corrida completa paralela tuvo un timeout en `SupervisorRestartsAnExitedChildAndCancellationLeavesItsReplacementRunning`; el test pasó aislado y en la corrida completa serial. Sin omitir pruebas.
- `scripts/test-installer.ps1`: pasó; `scripts/test-browser-extension-package.ps1 -Version 0.1.92`: pasó.
- Producción UI: TypeScript, build, CSP y contraste pasan. Host `--smoke`, `--smoke-renderer-failure`, `--smoke-renderer-crash`: código0.
- Setup/ZIP .92: manifiesto31/31, 1732 entradas, UI embebida verificada, ZIP Chrome/Edge14 entradas e iguales SHA-256.
- Recibo: `D:\TEMP\focusguard-fg166-validation\artifacts\fg270\build-receipt.json`.

## Estado y siguiente paso

El Host .92 todavía no está instalado. La prueba de bloqueo real de aplicación sólo se repetirá después de instalarlo y confirmar `Active` y `enforcementConnected=true`; entonces el proceso cooperativo TestTarget tendrá una vida límite finita. El perfil temporal se conserva para esa repetición y la sesión ya expiró. La navegación bloqueada en Edge permanece sin verificar.
