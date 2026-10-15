# Fase 3: auditoría de alcance pendiente

## Evidencia actual

La lista normativa de Fase 3 exige overrides locales y exportación/restablecimiento explícitos. El resumen previo que consideraba la aceptación física como único gate de producto era demasiado amplio.

- `cloud/sync/Core/SettingsReconciliation.cs`: `LocalSettingsOverrides` solo aplica valores a la proyección local. Las pruebas del núcleo validan la precedencia.
- `HostAccountService` y `HostSettingsSyncSession` componen la sesión con overrides vacíos. No hay acción bridge/UI para elegirlos, persistirlos o quitarlos.
- `cloud/sync/Core/SettingsExport.cs`: formato portátil, selección y confirmación preparados y probados. No hay llamada desde el Host ni diálogo para guardar/cargar el archivo. La transferencia E2EE manual entre dispositivos no sustituye este formato.
- Reset compartido, consentimiento, conflictos y estado Pending sí tienen integración Host/bridge/UI y cobertura local. La aceptación física sigue pendiente.

## Próximas tareas no bloqueantes

1. Integrar overrides por campo en almacenamiento local y proyección, con controles explícitos para usar el ajuste global o mantener un valor en esta PC. Los overrides no deben aparecer en ciphertext global ni impedir recibir nuevas revisiones. Quitar un override debe proyectar el último valor global; reinicio debe conservar la selección y revocación debe mantener preferencias locales. Revisar reset/conflictos para no saltarse esa precedencia.
2. Integrar exportación/importación portátil mediante diálogos nativos y el formato existente: preview, selección, confirmación, límites y rechazo de baseline obsoleto. Decidir explícitamente cómo una importación afecta el borrador global cuando sync está activa, respetando overrides. No exportar identidad, claves, rutas, permisos o políticas.
3. Validar ambas integraciones y regenerar un candidato versionado antes de pedir aceptación física completa.

Esta auditoría modifica documentación, no producto ni equipo. La suite Host 246/246 del checkpoint anterior no prueba estas dos integraciones ausentes.
