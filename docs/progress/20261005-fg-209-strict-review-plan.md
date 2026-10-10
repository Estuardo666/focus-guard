# FG-209 — Review strict y recuperación en React

## Objective

Port the existing finite strict-mode confirmation/recovery flow to the Host/React without moving authority out of the Windows service or weakening delayed recovery.

## Design

- Reuse the service's prepared-session protocol for both strict simple timers and strict focus cycles. Exact UTC end/phase boundaries and the two-minute preparation expiry come from the service.
- The Host generates the one-time recovery code and keeps its `RecoveryCredential` only in a short-lived review record bound to the current WebView document. React receives only the display code. Confirmation requires saved-code and unsaved-work acknowledgments plus exact profile-name text. The Host consumes the review before sending activation and never retries an unknown outcome.
- Cancel, renderer/document replacement, a fresh strict preparation and expiry clear the Host's in-memory record. The service preparation expires independently. Strict sessions cannot use normal stop/skip controls.
- Strict recovery stays a separate administrator-only utility. React will show its installed `%ProgramFiles%\FocusGuard\FocusGuard.Recovery.exe` location and manual steps. Never invoke it with elevation or automate UAC.
- Show current blocked application/site consequences, profile name, exact end, cycle plan and browser readiness. Preserve current break-rule behavior; reject invalid settings instead of converting them.

## Verification

## Resultado

Implementado para timers y ciclos. El Host conserva la credencial de recuperación sólo en un review efímero en memoria, limitado al documento WebView actual y al ID de preparación del servicio. React obtiene el código sólo para mostrarlo durante ese review y puede abrir el diálogo nativo para guardarlo localmente. La activación requiere reconocimiento de guardado, aviso de trabajo sin guardar, nombre exacto del perfil, preparación vigente y estado de servicio fresco. El Host consume la review antes de IPC y nunca repite una activación cuyo resultado se desconoce. Cancelar, cambiar de documento, iniciar otra review o expirar invalida el estado; el servicio también expira la preparación. Stop/Skip strict permanece deshabilitado. Help consulta y presenta la ubicación del recovery helper instalado, pero no lo abre ni solicita elevación.

Los ciclos strict reutilizan las reglas de descanso tal como están guardadas; una configuración no válida se rechaza y no se convierte automáticamente.

## Validación

- `npm test`: 12 archivos, 66 pruebas aprobadas.
- `npm run build`: TypeScript, Vite, allowlist de los 10 chunks locales, CSP y recursos de producción aprobados.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: 129 aprobadas, incluidas las pruebas de sesión strict sincronizadas al checkout principal.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings y 0 errores.
- `graphify update .`: 9,844 nodos, 24,898 aristas, 981 comunidades; `graph.html` se omite por superar el límite visual. `graph.json` y `GRAPH_REPORT.md` actualizados.
- `scripts/build-installer.ps1 -Version 0.1.58 -OutputDirectory D:\TEMP\focusguard-fg166-validation\artifacts\fg209\package-0.1.58`: paquete creado. Los 29 archivos del manifest existen; ZIP menú tiene 1,730 entradas y contiene `StrictReviewPanel.js`, `strict.js` y los demás chunks requeridos. El Setup autónomo y la copia dentro del paquete tienen el mismo hash y pasaron la comparación de hashes del payload incrustado.

SHA-256 Setup autónomo: `B03507533D9FEDD992ADAD007F7E7391B18A4E94C7D3371BEAD7E91BCFFABB6C`.

SHA-256 ZIP menú: `0FC5C044C6E66BDFE147FB2D15C0FE40484447BBFE9592589D903C40C2B4BFF8`.

No se instaló el paquete ni se alteró el servicio o una sesión del usuario. La aceptación instalada de LocalService/recuperación real permanece como gate separado. También siguen pendientes replay strict con una revisión nueva, semántica Block en descansos y la paridad restante de Fase 8/Fases 4–10.
