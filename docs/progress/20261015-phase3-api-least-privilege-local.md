# Fase 3: API y cursor con el rol PostgreSQL de mínimo privilegio

## Resultado

El fixture local de Fase 3 ahora ejecuta la API como `focusguard_product`, igual que el rol de producto creado por la provisión de Neon: LOGIN, sin superuser/CREATE DATABASE/CREATE ROLE y dueño de `focusguard_product`. El startup schema de la API crea `focusguard_device` y `sync_cursors` con ese rol.

La primera ejecución detectó una carrera del fixture: `pg_isready` podía responder durante el servidor temporal que usa la imagen oficial de PostgreSQL mientras termina la inicialización. Ahora el script reintenta conexión SQL, rol y base, y espera tres consultas estables con el rol de producto antes de arrancar el harness.

## Verificación

- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly -LocalPostgres -Phase3SyncOnly`: **20/20 PASS**.
- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly -LocalPostgres`: **152/152 PASS**, incluido OIDC/Keycloak, API, DPAPI, bootstrap/pairing, documentos E2EE/CAS y sync cursor/Host.
- Ambos harnesses pasan con el rol API no-superuser dueño de la base; la prueba verifica esos atributos antes de iniciar la aplicación.
- El contenedor PostgreSQL efímero se elimina en ambos caminos; no se usa Neon, `gcloud` ni staging.
- Graphify: PASS; 1.961 archivos AST, 24.859 nodos, 182.651 aristas y 1.996 comunidades.

## Límites

Esto confirma que el API funciona con el tipo de rol definido por el aprovisionamiento. No observa los grants del servicio vivo de Neon. Antes del futuro deploy, después de la aceptación física, revisar el estado real del rol y del digest de API; la prueba local no sustituye esa comprobación.
