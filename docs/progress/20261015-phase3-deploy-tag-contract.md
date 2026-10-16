# Fase 3: preparar el contrato de despliegue de API

## Resultado

El script de despliegue ya admite una etiqueta explícita `phase3-*` para el backend de cursor y ajustes globales, sin aceptar despliegues sin versión. Se conservaron los controles existentes de cuenta/proyecto, digest actual, readiness y rollback.

## Cambios

- `Deploy-TncApi.ps1` ya no propone por defecto el tag antiguo de Fase 2; requiere `-ImageTag` explícito.
- Para `phase3-*`, después de readiness también verifica que `/v1/sync/cursor` anónimo responda 401 antes de guardar el pin nuevo; una ruta ausente o fallo activa el rollback.
- `TncApiDeploymentContract.psm1` valida tags `phase2-*`/`phase3-*` en minúsculas y limita su longitud.
- `Test-TncApiDeploymentContract.ps1` prueba tags aceptados/rechazados, la ausencia de un tag por defecto y que la validación ocurra antes de consultar `gcloud`.
- `scripts/test-installer.ps1` parsea y ejecuta el contract test offline.
- `cloud/README.md` alinea el registro con la última evidencia almacenada: API `.00016-gc7`, digest `cf4e600…`, que coincide con el pin en `gcp-staging.json`. Ese registro es histórico; el estado vivo deberá releerse antes de cualquier deploy.

## Verificación

- `cloud/deploy/Test-TncApiDeploymentContract.ps1`: PASS; verifica tags, rechazo sin tag y que el probe cursor de fase3 ocurre antes de escribir el nuevo digest; no ejecuta comandos Cloud.
- `scripts/test-installer.ps1`: PASS, incluidos contract test, 8 casos de readiness, smoke interactivo y paridad de archivos Chrome/Edge.
- `cloud/deploy/Test-ApiContainerSmoke.ps1`: PASS con API Docker/PostgreSQL efímeros; health live/ready 200, cursor sin bearer 401 y certificado TLS con SAN incorrecto rechazado. Contenedores y recursos temporales retirados.
- `git diff --check`: PASS.
- Graphify: PASS; 1.961 archivos AST, 24.859 nodos, 182.651 aristas y 1.990 comunidades.
- No se ejecutó ningún comando Cloud, `docker push` ni despliegue. El contract test invocó `Deploy-TncApi.ps1` sin tag para confirmar que falla antes de consultar `gcloud`; staging no se contactó.
- No cambió el cliente Windows ni se regeneró `.183`.

## Límites

La prueba no determina si el digest del servicio vivo todavía coincide con `gcp-staging.json`. Antes de desplegar, tras completar la aceptación física, el preflight debe leer el servicio real; un digest distinto seguirá causando rechazo hasta reconciliarlo deliberadamente. No se desplegó la ruta de cursor ni se declara staging aceptado.
