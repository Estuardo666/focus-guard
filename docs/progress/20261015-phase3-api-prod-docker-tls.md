# Fase 3 — API Docker en configuración Production con TLS

## Resultado

Se reforzó `cloud/deploy/Test-ApiContainerSmoke.ps1` para ejecutar la imagen oficial Linux/amd64 del API en `ASPNETCORE_ENVIRONMENT=Production`. El test genera una CA y un certificado efímeros con SAN `127.0.0.1`, levanta PostgreSQL 17 con TLS y configura Npgsql con `SSL Mode=VerifyFull;Root Certificate=...`. La ruta de startup aplica su política de producción normal y `/health/ready` solo puede responder 200 después de ejecutar una consulta real contra PostgreSQL.

La prueba también consulta `/health/live` (200) y `/v1/sync/cursor` sin bearer (401), confirmando que la ruta nueva está publicada y protegida. Luego intenta iniciar una segunda instancia con la misma CA pero `Host=localhost`, mientras el certificado solo contiene el SAN `127.0.0.1`; Npgsql rechaza la conexión por mismatch y el API no completa su startup. Esto comprueba tanto la conexión TLS válida como la verificación de hostname. La escritura/lectura autenticada del cursor sigue cubierta por el harness separado de Keycloak/PostgreSQL.

## Verificación

- `cloud/deploy/Test-ApiContainerSmoke.ps1` — PASS en `Production`; health live 200, ready 200 y cursor anónimo 401. La conexión con certificado SAN incorrecto falla de forma esperada.
- El API y PostgreSQL se ejecutaron en contenedores Docker efímeros. El script eliminó ambos contenedores, el volumen nombrado, la imagen API y el directorio temporal de certificados.
- No se llamó a Neon/staging, no se instaló software y `.173` no cambió.

## Límite

El certificado TLS de PostgreSQL se crea para el test local y no representa la CA ni el hostname de staging. La aceptación física entre las dos PCs y la prueba contra el backend administrado siguen pendientes, en ese orden.
