# Fase 3 — dos runtimes sobre HTTPS loopback con pinning

## Resultado

Se extendió `SettingsHttpsLoopbackTests` para que dos `SettingsAutomaticSyncRuntime` se conecten al mismo Kestrel local mediante `SettingsDocumentHttpTransport` real. Cada dispositivo tiene DeviceId, token de laboratorio, digest de cuenta, secreto de sesión, proof de request y clave de autor distintos; ambos comparten accountId/generation, contenido cifrado y membresía sintética.

El certificado generado para el ensayo se pinnea en `HttpClientHandler`. Middleware verifica el token sintético, session hash, DeviceId, tiempo, anti-replay y proof firmado de cada solicitud. El ledger valida la firma FG-E2EE-1 del dispositivo vigente y CAS. Después de inicializar y sincronizar el tema, el segundo runtime importa el documento, cambia idioma y publica la nueva revisión; el primero la recibe tras una señal de reconexión. El servidor conserva solo el documento cifrado. Finalmente ambos loops se cancelan y disponen antes de la prueba existente de tombstone.

## Validación

- La prueba `ActualPinnedHttpsCarriesSignedCasAndTwoRuntimeConvergenceWithoutPlaintextServer` pasó **5 veces consecutivas**.
- Suite completa `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore`: **136/136 PASS**.
- `dotnet format` focalizado del archivo de prueba, modo verificación: PASS.

## Límites

El token y la membresía son sintéticos; no participan Keycloak, API staging ni Neon. Los dos clientes corren bajo el mismo Windows y usuario, así que DPAPI no demuestra transferencia entre PCs. El test tampoco conecta ni activa la app instalada. La aceptación física de Fase 2 propietario→hermano sigue abierta.


Actualización posterior: la misma prueba ahora incluye conflicto real entre dos ediciones simultáneas de duración y `NotifyConflictResolved`. Para el resultado actual de **139/139** y cinco ejecuciones verdes, ver `20261014-phase3-conflict-wakeup.md`.