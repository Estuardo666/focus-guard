# Fase 3 — cancelación segura del consentimiento desde el bridge

## Resultado

Se añadió una regresión que activa `account.settings.sync.enable` a través del `NativeBridgeDispatcher` real y deja bloqueado el prompt nativo del `HostAccountService`. Al rechazar el prompt, la respuesta de cuenta pasa de `Enabling` a `Cancelled`; el opt-in no crea la sesión de sincronización, vault DPAPI del dispositivo ni vault de identidad.

La prueba también compara las preferencias guardadas antes y después byte/valor por valor, verifica que `SettingsSyncEnabled` permanece apagado y que no se expone un error de bridge. Esto protege el principio de consentimiento explícito: cancelar no inicia lectura de membresía, baseline ni publicación.

## Verificación

- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CancelledNativeConsentLeavesUserSettingsAndDeviceVaultUntouched --verbosity minimal` — **1/1**.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --verbosity minimal` — **241/241**.
- `dotnet format whitespace FocusGuard.slnx --verify-no-changes --no-restore --include tests/FocusGuard.Host.Windows.Tests/HostSettingsSyncConsentBridgeTests.cs` — PASS.
- No se instaló ni desplegó `.173`, no se llamó a staging y no se modificó el vault/estado real del usuario.

## Límite

La prueba cubre el puente y el servicio de cuenta en un Host local, pero no requiere las dos PCs ni prueba convergencia. La aceptación física sigue pendiente en el [runbook de Fase 3](../cloud-phase0/PHASE3-PHYSICAL-ACCEPTANCE.md).
