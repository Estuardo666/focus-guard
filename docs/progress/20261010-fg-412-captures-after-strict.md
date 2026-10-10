# FG-412 — capturas bloqueadas después de una sesión estricta

## Hallazgo

El servicio publica `StatusResponse.StrictMode` como **tipo** de la sesión y lo mantiene en `true` cuando una sesión estricta llega a Expired/Recovered (observado en .138 durante FG-410). La UI ya lo tenía en cuenta: `SessionHistoryDetail` habilita los botones de captura usando solo la lista de estados terminales, y su prueba lo exige. Sin embargo, dos capas inferiores seguían tratando `strictMode` como «estricto en vigor»:

- `native/dashboard.ts` `resolveCapture`: rechazaba la acción con `CaptureUnavailable` sin enviarla.
- `Host.Windows/Bridge/SessionSummaryCommands.cs` `HandleCaptureActionAsync`: `status.StrictMode || !CanStart(status)` devolvía `SessionStateChanged`.

Resultado: después de una sesión estricta, los botones «Abrir» y «Descartar» de las capturas aparecían activos pero fallaban hasta que empezara otra sesión.

## Corrección

En ambas capas se elimina la condición `strictMode` y se mantiene la comprobación de estado terminal (Inactive, Expired, Stopped, Recovered, Failed). Esa lista ya excluye Active, Activating y RecoveryPending, así que las capturas siguen bloqueadas mientras la sesión estricta está en vigor. No cambia el significado del campo IPC, del que dependen la validación de `IpcContracts`, el bridge y el drill FG-112.

Revisados y sin cambios: `ActiveSessionCommands` (exige Active), la intención programada (las programadas nunca son estrictas), `SessionJournalWorkflow`, las validaciones de `BridgeContracts` y la cabecera de `App.tsx`. Esta última sigue mostrando «Sesión · Estricto · Expirada» tras una sesión estricta; es solo informativo y no bloquea nada.

## Pruebas

- `CaptureActionCommandTests` (Host, nuevas): descartar tras Expired/Recovered estrictos funciona y llama a `ResolveCaptures` (antes fallaba con `SessionStateChanged`); con Active, Activating o RecoveryPending estrictos sigue devolviendo `SessionStateChanged` sin resolver.
- `dashboard.test.ts`: con Active o RecoveryPending estrictos no se envía ninguna petición; con Expired se envía `capture.dismiss` y se recarga el resumen (antes no se enviaba).
- Host 161/161; React 148/148 y build (tsc, vite, verify-dist); .NET Debug 914/914 sin warnings.

## Limitaciones

No reverificado en la app instalada: el instalador .139 se generará junto con FG-413 y la Fase 8.
