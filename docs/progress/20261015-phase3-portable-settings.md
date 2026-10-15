# Fase 3 — exportación e importación portátil integrada

## Resultado

Se conectó el formato `SettingsExport` con Host, bridge y UI. La sección de archivo portátil funciona sin iniciar sesión. Exportar requiere confirmación nativa y escribe un JSON de solo tres preferencias. Importar abre un diálogo nativo, limita la lectura a 8 KiB, valida el formato y muestra una vista previa con selección explícita por campo. El bridge no acepta rutas ni valores de archivo desde la página.

Los campos no seleccionados y las preferencias locales restantes se conservan. Con sincronización automática consentida, la importación usa `SettingsAutomaticSyncCoordinator.ImportReviewedAsync`, que valida el baseline efectivo y actualiza outbox/proyección bajo su coordinación atómica. Sin sincronización activa, `DesktopSettingsStore.TryApplySharedIfUnchanged` compara tema, idioma y duración y aplica la selección bajo el lease de archivo compartido.

## Archivos cambiados

- `src/FocusGuard.Host.Windows/Hosting/HostAccountService.cs`: acciones, diálogos mediadores, lectura limitada, preview, importación sync/local y estados.
- `src/FocusGuard.Host.Windows/Hosting/PortableSettingsTransferDialog.cs`: selección de archivo nativa, confirmación de exportación y preview de importación.
- `src/FocusGuard.Host.Windows/MainForm.cs`, `Bridge/BridgeRequestParser.cs`: conexión Host/bridge con acciones sin parámetros.
- `src/FocusGuard.Host.Windows/Resources/HostStrings*.resx`: copy nativo inglés/español.
- `src/FocusGuard.Client/Preferences/DesktopSettings.cs`: comparación y actualización de campos compartidos bajo un solo lease.
- `src/FocusGuard.UI/src/AccountPanel.tsx`, `native/account.ts`, `AccountPanel.test.tsx`, `locales/en.json`, `locales/es.json`: tarjeta local, acciones y estados.
- `tests/FocusGuard.Host.Windows.Tests/AccountBridgeTests.cs`, `DesktopSettingsStoreAtomicUpdateTests.cs`.

## Pruebas y comandos

- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj --no-restore --filter FullyQualifiedName~AccountBridgeTests` — PASS, 13/13.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj --no-restore --filter FullyQualifiedName~DesktopSettingsStoreAtomicUpdateTests` — PASS, 1/1.
- `npm run check` — PASS.
- `npm test -- src/AccountPanel.test.tsx src/native/account.test.ts` — PASS, 24/24.
- El build de Host ejecutó el build de assets UI y validaciones CSP/accesibilidad existentes — PASS; Vite mantuvo el aviso existente de chunk mayor a 500 kB.

## UX y seguridad

La acción está disponible sin cuenta y no activa la sincronización. La exportación es un archivo JSON legible; la confirmación explica el contenido y pide mantenerlo privado. Importación inválida, mayor a 8 KiB, cancelada o con baseline obsoleto no cambia preferencias.

El cifrado E2EE de la transferencia manual sigue siendo un flujo distinto. Overrides locales y su UI son un hito separado de esta integración; el coordinador recibe el estado durable disponible y los imports seleccionados siguen su precedencia. La aceptación física entre dos Windows sigue pendiente.

## Próxima tarea

Revisión adicional del coordinador: `ImportReviewedAsync` compara el baseline efectivo bajo el lease del outbox antes de escribir, requiere consentimiento vigente y respeta overrides por campo. Regresiones comprueban una edición concurrente que deja obsoleto el preview sin mutar preferencias/outbox, importación mixta local/global y ausencia de wake-up cuando solo cambia una excepción local. Suite Sync 158/158 PASS y formato whitespace focalizado PASS.

Pruebas directas del servicio añadidas en `HostPortableSettingsTests`: exportación sin sesión, importación parcial conservando preferencias locales y rechazo de cancelación/JSON inválido/tamaño excesivo/preview obsoleto. Los siete casos PASS, incluida corrupción. La teoría de creación/reinicio del Host también importa duración y comprueba publicación cifrada con sesiones vigentes/renovadas; suite Host258/258 PASS. Validación y hashes finales en `20261015-phase3-windows-175.md`.

Completar integración de controles de overrides locales en Host/bridge/UI y ejecutar la aceptación física Fase 3 según el runbook. No se generó instalador ni se instaló o desplegó nada en este checkpoint.
