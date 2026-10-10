# FG-279 — Fase 5: runtime ausente en el Host .94 instalado

## Resultado

FocusGuard.UI.exe .94 instalado ejecutó --smoke-runtime-missing y salió con exit code0. El smoke valida la aparición del panel de error nativo esperado cuando WebView2 Runtime no está disponible, y que el Host puede cerrar limpiamente.

## Estado del producto

Antes y después, el Service conservó PID18792, Ready/Running e Inactive sin SessionId. El Host normal siguió PID38352 y Agent PID45296 siguió como hijo del Host. El smoke usa una ruta runtime vacía en su escenario de prueba; no afectó la instalación ni la sesión normal.

## Comando y evidencia

C:/Program Files/FocusGuard/FocusGuard.UI.exe --smoke-runtime-missing

Evidencia: D:/TEMP/focusguard-fg166-validation/artifacts/fg278/installed-runtime-missing-0.1.94.json.

## Nota de verificación

El primer resumen de PowerShell intentó asignar a la variable automática Host y dejó vacío el campo NormalHostPidAfter. El proceso smoke sí completó con exit0. Una consulta separada por CIM y DrillClient confirmó PID38352, Agent45296 hijo del Host y Service Ready/Inactive; el artefacto conserva esta limitación.

## UX, seguridad y límites

No se inició ni detuvo sesión, no se cambió política ni se lanzó TestTarget. El error se valida por el harness, pero no se capturó visualmente el panel ni se probó una recuperación manual desde la ventana normal. La aceptación visual y DPI sigue pendiente.

## Siguiente tarea

Completar aceptación visual/teclado/forced-colors de la ventana normal .94 y cerrar/reabrir el Host durante una sesión normal activa. El rollback owner-present continúa pendiente en contexto elevado.