# Fase 2 — Host valida la historia de autor de ajustes

## Resultado

`HostAccountService.MembershipSnapshot` conserva `MembershipChain` del endpoint `/v1/bootstrap/membership`. `ValidateMembershipSnapshot` verifica la cadena cuando viene presente contra issuer/account/generation, revisión y digest local del vault. Al abrir un documento de transferencia de ajustes existente, Host exige una cadena no vacía y resuelve `AuthorDeviceId` en `document.MembershipRevision` mediante `PinnedSettingsAuthor`; la firma no se acepta solo porque el dispositivo figure en la membresía actual. Si faltan revisiones históricas o hay transición no válida, falla cerrado con `AccountSettingsDocumentInvalid`.

El API fuente del workspace ya incluye la cadena en la respuesta. No se desplegó una nueva revisión de staging. La sincronización automática todavía no está conectada al Host; este cambio asegura la ruta existente de transferencia única y prepara la validación que requerirá sync.

## Validación

- `HostMembershipHistoryTests`: deserializa la cadena del DTO del Host, valida membership revision2 con el digest local y rechaza una cadena truncada.
- Suite `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: **222/222 PASS**.
- `scripts/build-installer.ps1 -Version 0.1.161`: PASS.
- `scripts/test-installer.ps1`: PASS.
- `FocusGuard-0.1.161-Setup.exe --package-self-test`: exit 0.
- Setup .161: 379.757.480 bytes, SHA-256 `5E7B16DE45070B09926D6CB02605E3784D96E817A53126D6D95A6A4200C1C508`.
- ZIP .161: 570.837.715 bytes, SHA-256 `0EF641D4D37C8B1AEFF2605BE7F306AB80D0FA397BB268BA9A5025D92BA0316C`.
- Host DLL del candidato: 635.392 bytes, SHA-256 `36416972B0EEC23F3E2F6067A3CB2C773A656AA4757F9B57E6EB1F4966E397F0`.

El candidato no se instaló; no se cambió el Host de la máquina ni se accedió a cuentas/staging.

## Límites

El Host aún no crea `SettingsAutomaticSyncRuntime`, no guarda su outbox/consentimiento con el tick nuevo y no propaga cambios de `DesktopSettingsStore` al scheduler. La aceptación física de Fase 2, propietario→hermano, sigue abierta.
