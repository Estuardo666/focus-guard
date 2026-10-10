# FG-271 — Tolerancia del test de reinicio del ProcessAgent

## Hallazgo

Una corrida de `dotnet test FocusGuard.slnx` en paralelo agotó el límite de7s de `SupervisorRestartsAnExitedChildAndCancellationLeavesItsReplacementRunning`. El test pasó aislado, pasó con el resto de `DesktopLifecycleTests` y pasó en la solución completa serial. La suite paralela inicia procesos .NET en varios proyectos simultáneamente, por lo que el límite del test no tolera arranque lento bajo carga.

## Plan y aceptación

1. Conservar el mismo escenario real de salida, reinicio, cancelación y reemplazo vivo.
2. Dar hasta30s al ciclo supervisado y márgenes adecuados al inicio/limpieza de procesos.
3. Ejecutar el test de forma aislada y luego la suite paralela completa; no desactivar ni filtrar la prueba.

No cambia código de producto y no requiere regenerar Setup.

## Resultado

Se amplió el máximo del ciclo de prueba a30s, la espera del proceso sustituto a25s y los márgenes de salida a10s. El test pasó aislado y la solución completa paralela pasó875/875. La primera corrida paralela, con límites anteriores, había fallado sólo en esa espera; la suite completa serial ya había pasado875/875.
