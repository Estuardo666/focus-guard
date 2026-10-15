# Fase 3: editar un override local durante revisión de reset — plan

## Brecha

La prueba Host ya cubre rebase y cancelación del tombstone; la integración dual Host cubre overrides antes del reset. Falta validar una edición local de un campo ya overridden después de abrir la revisión de conflicto y antes de enviar la elección.

## Entrega prevista

Extender `HostResetConflictCanRebaseTheTombstoneOnlyAfterTheReviewedChoice`: iniciar con tema/idioma/duración overridden, mantener el estado de conflicto/reset y editar la duración local por `TrySyncSharedSettingsEditAsync` mientras la UI tiene una revisión abierta. Luego elegir mantener el reset y comprobar que el tombstone rebasa la revisión remota, conserva el override recién editado, actualiza `DesktopSettings`, y no crea draft compartido ni transport writes por la edición local.

Seguridad y recuperación: la resolución valida revisión/draft/conflictos globales y usa el outbox vigente bajo lease. Los overrides son locales y no modifican el tombstone compartido; consentimiento y recovery permanecen intactos.

## Aceptación

Regresión Host falla si se pierde la excepción local o si la edición se publica; pasa al resolver el conflicto con el valor local más reciente. Ejecutar test focalizado, suite Host, formato y graphify. No cambia producto distribuible, instalación o staging.
