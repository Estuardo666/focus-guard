# Fase 3: importación portátil con sync revocada y override local

## Hallazgo y alcance

La importación JSON válida con sync desactivada aplicaba los campos seleccionados a `DesktopSettings`, pero no actualizaba un override local DPAPI del mismo campo. Al reactivar sync, ese valor viejo podía proyectarse de nuevo sobre la importación.

## Cambio

Después de aceptar el preview y validar que `DesktopSettings` conserve su baseline, el Host mapea los campos seleccionados a `SettingsFields` y pasa la propuesta al mismo helper protegido para editar overrides con consentimiento revocado. El helper actualiza solo campos que ya estaban overridden, dentro del outbox DPAPI por cuenta/dispositivo y bajo revalidación de consentimiento apagado. La importación se aplica luego con compare-and-apply del archivo de preferencias.

El flujo no activa sync ni runner, no crea un outbox y no publica. Los campos sin override mantienen el comportamiento local previo. Si no existe outbox revocado ni una excepción para el campo, no añade estado nuevo al vault.

## Prueba red/green

La prueba `RevokedSyncPreferenceEditUpdatesOnlyExistingLocalOverridesWithoutNetworkWork` se extendió después de la edición por `preferences.set`: importa solo el tema desde un JSON mientras consent está apagado, comprueba actualización del override DPAPI y ausencia de transporte, luego reactiva la sesión y espera la proyección. Antes del fix, la assertion del override falló (`expected dark`, `actual light`); después pasa y el tema permanece `Dark`.

## Validación y recuperación

Prueba focalizada y suite Host completa **259/259 PASS**; React/UI **254/254 PASS**, TypeScript/build PASS, formato Host y pruebas PASS. Instalador autónomo `.179` y ZIP menú regenerados desde la fuente formateada; `scripts/test-installer.ps1`, guard de producción y `--package-self-test` PASS. Hashes SHA-256: Setup `F4F174BE03E89ED228CF9C34A2C0FA202BAF9BB7F097F10E94B0AEE3F1B31D91`; ZIP `BCD9CFA04384CD92F4C29497D97290ED4B3A82D43F77A18CD0879896EA90271B`. Graphify actualizado. Paquete candidato local, no instalado ni desplegado. El outbox sigue siendo estricto y conserva el aviso de downgrade `.174`.
