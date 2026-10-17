# Preparación local Fase 3: verificar salud tras rollback

## Plan

1. Revisar el camino actual de rollback de `Deploy-TncApi.ps1` y su contrato offline.
2. Tras restaurar y esperar la revisión previamente fijada, comprobar `/health/live` y `/health/ready` antes de declarar recuperado el servicio.
3. Extender el contrato automatizado para exigir el orden de verificación y actualizar la guía de operaciones.
4. Ejecutar la suite local del instalador y refrescar graphify; no invocar Cloud/staging.

## Estado inicial

El rollback esperaba que Cloud Run declarara lista la revisión con la imagen anterior, pero no repetía los smoke checks HTTP de liveness y readiness que se exigen para la nueva revisión.

## Riesgo y alcance

Una revisión lista no demuestra por sí sola que ambas rutas de salud sigan respondiendo. Si la revisión previa no pasa esos checks, el script debe informar la falla de recuperación; no debe presentar el rollback como verificado. El cambio afecta únicamente la preparación de despliegue, sin alterar servicios ni contenedores.

## Implementación y verificación

- Tras confirmar que Cloud Run publicó la revisión con la imagen anterior, `Deploy-TncApi.ps1` restaura el pin local y verifica `/health/live` y `/health/ready` antes de relanzar el error original del despliegue.
- El contrato offline exige que ambas comprobaciones ocurran dentro del rollback y después de esperar la revisión restaurada/restaurar el pin. El test continúa sin invocar Google Cloud.
- `cloud/README.md` y `TASKS.md` documentan el comportamiento.

Verificación:

- `cloud/deploy/Test-TncApiDeploymentContract.ps1` — PASS.
- `scripts/test-installer.ps1` — PASS; incluye el contrato actualizado.
- `graphify update .` — PASS; 24.860 nodos y 182.673 edges.
- `git diff --check` — PASS; con advertencias normales de conversión LF/CRLF.

No se ejecutó deploy ni smoke contra staging; el gate físico de Fase 3 sigue pendiente.

## Consecuencias

- **Operación:** si la imagen previa llega a estado Ready pero una ruta de salud falla, el operador recibe ese fallo de rollback y el script no afirma que la recuperación esté verificada.
- **Seguridad/recuperación:** se mantiene el pin de la imagen restaurada antes del smoke; así la referencia local coincide con el servicio aunque el smoke reporte una falla residual.
- **Límite:** el contrato de orden es local. La respuesta de Cloud Run debe observarse cuando se ejecute el deploy autorizado tras completar la aceptación física.
