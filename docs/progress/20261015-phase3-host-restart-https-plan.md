# Plan — reanudar dos sesiones Host desde DPAPI por HTTPS

La sesión nueva ya se probó aislada con transporte de memoria. Ahora se completa el mismo gate tras convergencia y reset compartido en el test HTTPS de dos Hosts.

1. Cerrar ambos loops/sesiones después de publicar y recibir el tombstone.
2. Cargar ambos vaults DPAPI en sesiones nuevas, sin volver a llamar `EnableAsync`.
3. Iniciar `RunAsync` y comprobar consentimiento/outbox persistidos, relectura HTTPS del tombstone y ausencia de un POST o revisión nueva.
4. Forzar una edición remota mientras un dispositivo mantiene un reset offline; resolver el conflicto rebasando el tombstone por la ruta Host.
5. Verificar el tombstone final y la reanudación de ambos outboxes; correr Host, Sync, UI, formato y graphify.

Solo tests/docs; el Setup `.170` sigue siendo el candidato. No instalar, desplegar ni llamar staging.

Estado: completado en [el checkpoint de reset global y reanudación HTTPS](20261015-phase3-global-settings-reset.md).
