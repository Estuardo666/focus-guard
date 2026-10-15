# Plan — solicitud explícita para restablecer ajustes globales

El plan exige «Restablecer ajustes globales» con vista previa sin alterar snapshots activos (`docs/PLAN-CLOUD-MULTIDISPOSITIVO.md`, sección 9.2, punto 8; tarea Fase 3, punto 5). El núcleo ya puede crear un tombstone, y el Host resuelve conflictos de reset, pero no existe una acción de usuario que inicie la operación.

1. Añadir una ruta HostSession/Core que, con consentimiento activo y preview vigente, guarde el tombstone y proyecte solo theme/language/default duration bajo el lease del outbox; preservar campos locales y el estado de sesiones activas.
2. Pedir una confirmación nativa con preview de los valores actuales y predeterminados; cancelar debe dejar outbox y preferencias byte por byte iguales.
3. Añadir un botón disponible solo cuando la sync esté activa y estable, más una acción bridge exacta sin parámetros.
4. Probar cancelación, reset, privacidad/local fields, wake-up del scheduler, conflictos/stale preview y contrato bridge/UI; validar Host/Sync/UI, formato y grafo.
5. Regenerar y validar el Setup standalone porque cambia un componente distribuido. No instalar, desplegar ni tocar staging.

Estado: completado en [el checkpoint del reset global](20261015-phase3-global-settings-reset.md).
