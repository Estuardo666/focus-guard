# Fase 3 — sync context con historia firmada de membresía

## Resultado

Se agregó una prueba Windows que crea dos DeviceIdentity y dos revisiones de membresía firmadas. El segundo device se une en revision2; su vault DPAPI contiene el contenido/key epoch vigentes y sesión. `SettingsSyncContextFactory` valida el historial completo contra `MembershipRevision` y `MembershipDigest` del vault. Ese dispositivo abre un documento de ajustes E2EE firmado por el primer dispositivo en revision1, y prepara/abre un nuevo documento como autor en revision2.

La prueba también confirma que no se puede crear el contexto con solo la membresía actual: sin la revisión histórica del autor, se rechaza el documento/contexto.

## Validación

- Prueba dirigida `PinnedSettingsAuthorTests`: **3/3 PASS**.
- Suite completa `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore`: **143/143 PASS**.
- Formato focalizado del test en modo verificación: PASS.

## Límites y siguiente integración

Los miembros y documentos son sintéticos bajo el usuario Windows actual; no se llamó a Keycloak, API staging o Neon ni se usó otra PC. `HostAccountService` hoy deserializa el snapshot actual de membresía en un DTO que omite `MembershipChain`; el futuro caller de sync debe conservar/verificar esa cadena antes de llamar `SettingsSyncContextFactory`. No está conectado al Host. Fase 2 sigue esperando verificación física propietario→hermano.
