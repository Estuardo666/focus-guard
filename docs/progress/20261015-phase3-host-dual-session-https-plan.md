# Plan — convergencia HTTPS a través de dos sesiones Host

Fase 3 ya cuenta con una prueba HTTPS de dos runtimes y pruebas Host separadas. El paso local es unir esas dos capas con `HostSettingsSyncSession`, sin staging ni PCs físicas.

1. Crear dos identidades/device vaults DPAPI bajo una cuenta y cadena de membresía firmada válida.
2. Ejecutar un Kestrel HTTPS de loopback con certificado fijado por el cliente, bearer synthetic, prueba de posesión por dispositivo, ETag/CAS y almacenamiento exclusivo del documento cifrado.
3. Componer dos sesiones Host con scopes/outboxes/preferencias separados y un transporte HTTP auténtico; consentir explícitamente en ambas.
4. Iniciar ambos loops, editar un ajuste global en el primero y despertar el segundo por reconexión; comprobar convergencia en ambos outboxes y archivos locales, preservando preferencias no compartidas.
5. Ejecutar Host/Sync/UI, formato y grafo; actualizar plan/TASKS/checkpoint. No instalar, desplegar, llamar staging ni usar credenciales reales.

Estado: completado en [el checkpoint de convergencia Host HTTPS](20261015-phase3-host-dual-session-https.md).
