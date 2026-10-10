# FG-374 — Preflight de rollback .128 → .123

## Objetivo

Comprobar sin mutaciones que el paquete candidato .128 y la instalación .123 cumplen las precondiciones del rollback drill antes de una ejecución owner-present.

## Resultado

`invoke-installer-rollback-drill.ps1 -PackageDirectory D:\TEMP\FocusGuard-0.1.128-fg360-schedule-state -PreflightOnly` pasó:

- Candidato .128: manifiesto válido y 31/31 archivos presentes.
- Instalación actual .123: servicio Running como `NT AUTHORITY\LocalService`, PID5732.
- Diagnostic: SessionState `Expired`, Strict=false.
- ClientMetadataQueryConsent ya está registrado.
- `InstallerInvoked=false`; `MachineStateChanged=false`.

Esto no demuestra la restauración tras una falla inyectada; el rollback real .128→.123 requiere un contexto elevado utilizable y sigue pendiente.

Evidencia estructurada: `artifacts/fg374-output/rollback-preflight-0.1.128.json`.
