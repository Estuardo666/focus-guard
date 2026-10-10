# FG-319 — Separar Diagnostic del control mutador

Fecha: 2026-10-08

Estado: corrección, tests y paquete .106 completos; aceptación instalada pendiente.

## Motivo

El contrato de `UX_SPEC.md` exige que Strict muestre consecuencias, recuperación y una frase aleatoria que debe escribirse antes de activar. El rol Diagnostic autenticaba como cliente de Control con el allowlist completo, y `FocusGuard.DrillClient.exe activate-strict` podía activar Strict sin esa revisión. El antiguo FG-112 reboot drill también iniciaba la sesión por ese comando y fabricaba un acknowledgement NativeMessaging.

## Cambio

- `IpcAuthorizationPolicy` permite a Diagnostic sólo `StatusRequest` y `ActivityRequest`; Desktop y WebViewHost conservan el allowlist mutador.
- El DrillClient de producción rechaza `activate-strict` y `acknowledge` antes de crear la conexión autenticada. El cliente aún puede consultar status/actividad y realizar el probe negativo del Agent.
- La prueba `DiagnosticActivationDenialPrecedesDispatchAndKeepsListenerAvailable` confirma que una activación denegada no llega al dispatcher, la sesión queda `Inactive` y el listener sigue respondiendo a `StatusRequest`.
- FG-112 ahora requiere una sesión Strict ya iniciada mediante la UI revisada. El post-reboot drill espera status/acknowledgements de canales reales; no fabrica un mensaje NativeMessaging.
- El preflight de FG-112 es sólo lectura y no arma la sesión si el estado no cumple. Arm/complete requieren presencia del propietario; arm requiere una consola ya elevada y sólo imprime el comando de reinicio, no reinicia el equipo.

## Verificación local

- `IpcAuthorizationPolicyTests`: 79/79 pasan. Recorre todo `IpcMessageType` y sólo permite Status/Activity a Diagnostic.
- `DiagnosticActivationDenialPrecedesDispatchAndKeepsListenerAvailable` pasa por un pipe local: la activación se deniega antes del dispatcher, la sesión permanece `Inactive` y el listener sigue respondiendo a status.
- `scripts/test-installer.ps1`: parse y contratos de ambos reboot drills pasan; no se encuentran llamadas a `activate-strict` ni `acknowledge`.
- Drill FG-112 `-PreflightOnly` contra .102 informa `Ready=false` porque la sesión está `Expired`; `MachineStateChanged=false`.
- `dotnet test FocusGuard.slnx -c Release --no-restore`: 14 ensamblados,877/877.
- React114/114, TypeScript/Vite, contraste, viewport720×480, CSP y recursos locales pasan.
- `scripts/test-installer.ps1` y `scripts/test-browser-extension-package.ps1 -Version 0.1.106` pasan; cuatro Host smokes .106 exit0.
- Manifest .106 lista31 archivos; Setup y ZIP se generaron; hashes de cuatro recursos UI coinciden con dist. El DrillClient .106 devuelve exit1/mensaje específico para `activate-strict` y `acknowledge`; la guarda precede a `VerifiedPipeConnectionFactory`.
- No se ejecutaron strict, reboot, ack sintético ni instalación.

## Pendiente

El recibo está en `artifacts/fg319-output/fg319-build-receipt.json`; el Setup está en `artifacts/fg319-output/package/FocusGuard-0.1.106-Setup.exe`. El equipo sigue instalado en .102, donde la política antigua todavía autoriza el alcance Diagnostic amplio. Instalar .106 y comprobar que status/activity siguen disponibles y los mutadores son denegados por el Service real. Después probar NativeMessaging y Agent positivos, otros roles/caller negativos, continuidad y rollback .102.

El ejecutable `C:\Program Files\FocusGuard\FocusGuard.DrillClient.exe` tiene el mismo SHA-256 que `FocusGuard.DrillClient.exe` del paquete .104 anterior a esta restricción. Eso confirma que la instalación .102 todavía contiene el comando `activate-strict`. No lo ejecuté; mantenerlo sin uso hasta instalar .106.
