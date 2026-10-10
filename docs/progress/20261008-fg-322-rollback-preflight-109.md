# FG-322 — Preflight de rollback del candidato .109

Fecha: 2026-10-08
Estado: DONE para la prevalidación de solo lectura; ejecución de rollback pendiente.

## Comando

`scripts/invoke-installer-rollback-drill.ps1 -PackageDirectory .\artifacts\fg321-output -PreflightOnly`

## Resultado observado

- CandidateVersion `0.1.109`; InstalledVersion `0.1.102`.
- 31 archivos listados en el manifest existen bajo el paquete candidato.
- FocusGuard Service `Running`, PID25096, cuenta `NT AUTHORITY\LocalService`.
- DrillClient `serviceReady=true`; sesión `Expired`; `StrictMode=false`.
- `InstallerInvoked=false`; `MachineStateChanged=false`.

Esto confirma que las condiciones de entrada del ensayo están listas, no que el rollback funcione. No se llamó al Setup, no se detuvo el servicio, no se inició sesión y no se modificaron perfiles. El ensayo de inyección real requiere un proceso PowerShell ya elevado y `-OwnerPresent`.

## Revalidación del candidato .111

El mismo modo `-PreflightOnly` se repitió contra `artifacts/fg326-output`: CandidateVersion0.1.111, InstalledVersion0.1.102, 31 archivos presentes, Service PID25096 Running/LocalService, `Expired`, Strict=false, `InstallerInvoked=false`, `MachineStateChanged=false`. Recibo: `artifacts/fg322-rollback-preflight-111.json`. No es evidencia de restauración real.

## Revalidación del candidato .112

`-PreflightOnly` en `artifacts/fg328-output` vuelve a confirmar CandidateVersion0.1.112, InstalledVersion0.1.102, 31 archivos, Service PID25096 Running/LocalService, `Expired`, Strict=false, sin invocar Setup ni modificar máquina. Recibo `artifacts/fg322-rollback-preflight-112.json`; el rollback inyectado sigue pendiente.
