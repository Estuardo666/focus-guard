# Plan — recuperación del throttle de sync en Host

El scheduler puede detenerse en `NeedsAttention(Storage)` si el checkpoint DPAPI de backoff está corrupto. El núcleo ya tiene recuperación deliberada y no reanuda por sí sola; el Host todavía no la expone.

1. Conectar `SettingsRetryCheckpointStore.ResetAfterOwnerRecoveryAsync` y `SettingsAutomaticSyncRuntime.ResumeAfterRecoveryAsync` a la sesión Host, sin tocar el outbox.
2. Mostrar la acción solo cuando el reporte indique exactamente fallo Storage del checkpoint y consentimiento esté activo.
3. Pedir confirmación Windows con el alcance preciso y rechazar requests de bridge con parámetros no vacíos.
4. Probar cancelar, recuperar archivo corrupto, conservar outbox y reanudar scheduler; correr Host/Sync/UI e instalador.

No recuperar errores de protocolo/AEAD/outbox con esta acción. No borrar prefs ni staging; no desplegar ni instalar.

Estado: completado en [el checkpoint de recuperación Host](20261015-phase3-host-retry-recovery.md).
