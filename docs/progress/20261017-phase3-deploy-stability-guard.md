# Preparación local Fase 3: guard de revisión en curso

## Plan

1. Revisar el preflight actual de `Deploy-TncApi.ps1` y su contrato automatizado.
2. Rechazar localmente el deploy si Cloud Run no prueba que su revisión más nueva sea la revisión lista.
3. Probar estados estables, pendientes e incompletos con fixtures offline y mantener el guard dentro de las pruebas del instalador.
4. Actualizar `TASKS.md` y `cloud/README.md`; no invocar Google Cloud ni staging.

## Estado inicial

`Deploy-TncApi.ps1` comprobaba `Ready=True` y el digest fijado, pero no comparaba `latestCreatedRevisionName` con `latestReadyRevisionName`. Si hubiera una revisión creada pero aún no lista, el script podía iniciar un reemplazo desde un estado concurrente que no estaba completamente asentado.

## Riesgo y alcance

El cambio debe ser fail-closed: estados ausentes, revisión pendiente o condición Ready no verdadera impiden el reemplazo antes de construir/subir una imagen. Solo toca el contrato de despliegue de staging; no cambia el runtime de FocusGuard, no llama a servicios externos y no instala paquetes.

## Implementación y verificación

- `TncApiDeploymentContract.psm1` expone `Test-TncApiServiceStable`, que falla cerrado ante falta de estado Ready, nombres de revisión incompletos o revisión creada distinta de la última lista.
- `Deploy-TncApi.ps1` usa ese guard antes de construir/subir el reemplazo. El digest fijado y la ruta de rollback siguen sin cambios.
- `Test-TncApiDeploymentContract.ps1` cubre seis estados sintéticos y verifica que la comprobación sucede antes del build; se mantiene la prueba offline que rechaza una invocación sin tag antes de resolver `gcloud`.
- `cloud/README.md` documenta el nuevo preflight. No se invocó Google Cloud ni staging.

Verificación:

- `cloud/deploy/Test-TncApiDeploymentContract.ps1` — PASS; la ejecución sin tag se rechaza offline antes de buscar `gcloud`.
- `scripts/test-installer.ps1` — PASS; incluye el contrato de deploy, smokes de propietario/sesión y paridad Chrome/Edge.
- `graphify update .` — PASS; actualizó `GRAPH_REPORT.md` y `graph.json` (24.860 nodos, 182.673 edges).
- `git diff --check` — PASS; solo advirtió la conversión normal de LF a CRLF en archivos rastreados por Git.

## Consecuencias

- **UX/operación:** un operador recibe un error claro y debe esperar a que termine la revisión concurrente; el script no inicia un reemplazo sobre estado inestable.
- **Seguridad/recuperación:** reduce el riesgo de pisar un cambio de servicio ajeno al deploy; mantiene comparación de digest y rollback existentes.
- **Límite:** es validación de fixture y contrato local. No demuestra el estado actual de Cloud Run ni reemplaza la aceptación física previa a staging.
