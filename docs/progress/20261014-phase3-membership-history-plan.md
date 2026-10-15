# Fase 3 — plan de lectura con historia firmada de membresía

## Alcance

Probar el `SettingsSyncContextFactory` con un WindowsDeviceVault real y una historia sintética válida de dos revisiones: dispositivo A registra la cuenta; dispositivo B se agrega en la segunda revisión. B debe verificar un documento de settings que A firmó antes de que B existiera.

## Aceptación

1. Firmar y validar ambas membresías, digest y transición por ManageDevices.
2. Abrir con el contexto de B un documento E2EE firmado por A en revision1 usando el historial completo.
3. Rechazar el snapshot actual sin la revisión histórica necesaria.
4. Verificar que B pueda crear un documento en la membership revision vigente solo si tiene WriteSettings.
5. Guardar material B en DPAPI real; no usar OIDC, API staging ni datos del propietario.

## Límite

La membresía se genera sintéticamente en el test; la aceptación física de Fase 2 y la cadena descargada de la API quedan pendientes.
