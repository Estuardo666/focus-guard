# Fase 3 — checkpoint durable de backoff/reintentos

## Objetivo

Evitar que un reinicio del proceso Android/Windows borre el estado de backoff o `Retry-After` del scheduler preparado en Fase 3. Esta preparación local no conecta el scheduler con FocusGuard Desktop ni habilita sincronización.

## Implementación

- `SettingsRetryCheckpointStore` guarda sólo el contador de fallos y la duración, con DPAPI `CurrentUser`; la entropía y el contenido se ligan a issuer/account/generation/device. El archivo está limitado, rechaza JSON ambiguo y usa lease OS + reemplazo atómico.
- `SettingsRetryController` restaura la duración completa en un reloj monotónico nuevo después de cada reinicio. Puede esperar de más tras reinicios repetidos, pero no acorta el Retry-After por salto del reloj de pared.
- Conserva el checkpoint durante el cooldown y lo borra bajo lease sólo al vencer, antes del siguiente request. Local-change y reconnect no eluden el checkpoint durable.
- Errores de lectura/escritura/borrado se detienen como `NeedsAttention(Storage)`. El recovery explícito vuelve a cargar el checkpoint; un archivo corrupto sigue fallando cerrado.
- El checkpoint es opcional en el constructor para no activar persistencia en runtime accidentalmente. El Host todavía no lo instancia.

## Validación y límites

`dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore --nologo -m:1`: **129/129 PASS**. Los tests cubren cifrado/scope DPAPI, Retry-After conservado tras reinicio incluso con reloj de pared adelantado, falta de bypass por señal local, limpieza al vencer, corrupción/tamaño/temporales huérfanos fail-closed y consentimiento deshabilitado sin limpiar estado. `dotnet format` en verificación para Sync.Windows/Sync.Tests y `git diff --check`: PASS (avisos LF/CRLF preexistentes).

Pruebas en directorios temporales y DPAPI real del usuario actual. No tocaron datos reales, staging, API, cuentas ni preferencias. No prueba una caída eléctrica del volumen ni integra scheduler con consentimiento/runtime. Para las solicitudes donde falla el propio guardado local, el controller se detiene en `Storage`; después de recovery manual conserva el delay conocido en memoria. Si el sistema de archivos no pudo escribirlo y el proceso muere, no existe un checkpoint duradero que sobreviva; esa falla de almacenamiento requiere recovery y no puede ofrecer garantía de Retry-After entre procesos.

La persistencia se activa sólo si el caller inyecta un store con scope correcto. No conectar al Host hasta integrar consentimiento DPAPI, elegir una ruta estable bajo el perfil de usuario, validar ownership/ACL y cubrir el ciclo de vida/cierre del Host. Fase 2 conserva el gate físico Windows→hermano.

Un checkpoint corrupto se mantiene en `NeedsAttention(Storage)` incluso después de `ResumeAfterRecoveryAsync`; todavía no existe una API de reparación que permita al propietario revisar y borrar ese throttle corrupto de forma deliberada. La integración futura debe incluir esa ruta de recuperación antes de habilitar el store en runtime.
