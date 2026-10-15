# Fase 3 — aceptación local del API con PostgreSQL aislado

## Resultado

Se añadió `-LocalPostgres` a `cloud/deploy/Test-KeycloakNeon.ps1`. Combinado con `-NativeOnly`, levanta PostgreSQL 17 en un contenedor efímero y usa una base independiente para Keycloak y otra para la API. El harness habitual recorre OIDC, DPAPI, bootstrap/pairing, membresía, documentos cifrados, CAS, aislamiento por cuenta, anti-replay y revocación. Además, dos runtimes y dos `HostSettingsSyncSession` usan dispositivos, vaults DPAPI y E2EE reales contra `ContentDocumentLedger` y PostgreSQL; cubren convergencia, conflicto de duración/resolución, reset tombstone, proyección local y cursor account-wide. El cursor se incrementa dentro del CAS, vuelve en header/body, queda en el outbox DPAPI y no puede retroceder. El realm también sobrevive el reinicio del contenedor Keycloak durante la prueba.

Esta ruta no lee credenciales Neon ni ejecuta pruebas BFF. El puerto se elige dinámicamente y PostgreSQL solo se publica en loopback. La contraseña se genera para una sola ejecución. En éxito y en fallo, el script elimina los contenedores y el volumen anónimo temporal de PostgreSQL que creó.

## Política TLS de base de datos

El API conserva `SslMode.VerifyFull` por defecto. Solo acepta `SslMode.Disable` cuando se cumplen a la vez las tres condiciones: entorno `Development`, configuración explícita `SSL Mode=Disable` y host escrito como IP literal loopback. El test local de API ejercita esa excepción; staging/producción no la usan.

## Verificación

- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly -LocalPostgres` — **143 checks passed**: 125 de OIDC/API/DPAPI más 18 de sincronización/Host (15 de runtime/ledger con cursor y 3 de `HostSettingsSyncSession` a través de las rutas HTTP).
- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly -LocalPostgres -Phase3SyncOnly` — **18 checks passed** para iterar sincronización/cursor/Host sin iniciar Keycloak.
- La escritura y lectura del documento cifrado pasaron; se rechazaron revisión CAS obsoleta, replay, otra cuenta y dispositivo revocado. La base solo almacenó el sobre firmado/cifrado.
- El realm persistió después de reiniciar su contenedor Keycloak.
- Los dos clientes firmaron solicitudes de lectura/escritura; el ledger aceptó CAS, devolvió el conflicto de duración, y almacenó/aplicó un tombstone sin exponer `theme` ni `deleted` en claro.
- En el primer ensayo quedó un volumen anónimo de PostgreSQL; se identificó por hora de creación y label anónimo, se comprobó que ningún contenedor lo usaba y se retiró. Se corrigió la limpieza. La segunda ejecución confirmó que no quedan contenedores ni volúmenes anónimos nuevos.
- No se llamó a Neon ni a staging y no se instaló ni desplegó software.

## Límite

Los 15 checks de runtime llaman directamente al `ContentDocumentLedger` de producción con proofs firmados y PostgreSQL local, incluyendo persistencia/monotonicidad del cursor. Los tres checks Host atraviesan las rutas HTTP reales en TestServer con bearer/userinfo sintéticos e issuer HTTPS; los 125 checks separados ejercitan el API con Keycloak y JWT reales. No sustituye la aceptación física entre dos PCs ni staging.
