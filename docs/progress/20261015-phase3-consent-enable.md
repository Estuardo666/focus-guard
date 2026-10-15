# Fase 3 — opt-in aceptado desde el bridge

## Resultado

Se amplió el test `ConstructionIsInertAndExplicitOptInCreatesEncryptedBaselineAndConsent` para ejecutar la ruta de confirmación por `NativeBridgeDispatcher` → `HostAccountService` → `HostSettingsSyncSession`. Antes de confirmar, construir la sesión y rechazar `ownerConfirmed` siguen dejando el directorio sync sin crear y no hacen lecturas ni publicaciones. Al aceptar el prompt nativo, el bridge informa `Enabling`; el servicio termina la inicialización, guarda consentimiento DPAPI y establece `Enabled`.

La prueba verifica el documento inicial cifrado, una sola publicación del baseline, revisión 1, ausencia de borrador/en-vuelo y consentimiento persistido. La ruta de cancelación desde el mismo bridge tiene su prueba separada en `HostSettingsSyncConsentBridgeTests` y confirma que no se crea el vault del dispositivo ni se alteran preferencias.

## Verificación

- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ConstructionIsInertAndExplicitOptInCreatesEncryptedBaselineAndConsent --verbosity minimal` — **1/1**.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --verbosity minimal` — **241/241**.
- `dotnet format whitespace FocusGuard.slnx --verify-no-changes --no-restore --include tests/FocusGuard.Host.Windows.Tests/HostSettingsSyncSessionTests.cs` — PASS.

## Límite

El test usa una `HostSettingsSyncSession` válida ya compuesta y `MemorySettingsTransport` para aislar el tramo de consentimiento/servicio. No valida creación desde una sesión OIDC viva, el API staging ni dos PCs físicos; esas pruebas siguen pendientes según el [runbook de Fase 3](../cloud-phase0/PHASE3-PHYSICAL-ACCEPTANCE.md).
