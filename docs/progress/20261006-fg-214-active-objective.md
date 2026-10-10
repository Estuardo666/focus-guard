# FG-214 — Objetivo visible durante una sesión activa

## Alcance

Al pasar la sesión del servicio a `Active` o `Activating`, `DashboardStart` consulta una vez `session.intent.get` para el `sessionId` actual. Muestra el objetivo local como una línea secundaria debajo del perfil sólo si el ID de la intención coincide con el del servicio. Una respuesta para otra sesión nunca se mezcla con la sesión activa.

La pantalla activa sigue sin mostrar el formulario de inicio ni controles de stop/skip. Strict muestra la misma nota informativa y conserva las restricciones del modo. El countdown, fase final y estado de enforcement no cambian: el dato procede del journal de usuario y se presenta como texto.

## Validación

- `npm test`: 16 archivos, 74 pruebas aprobadas. La prueba activa confirma que la UI pide la intención, muestra el objetivo correcto y no envía prepare/start/activate.
- `npm run check`: TypeScript sin errores.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: 139 aprobadas; incluye intención de journal y límites del bridge.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings y 0 errors. CSP, allowlist de chunks y bundle local `host.js` 487.54 kB pasan.
- `graphify update .`: 9,944 nodos, 25,249 aristas, 990 comunidades; JSON/reporte actualizados. La visualización HTML se omite por el límite de nodos.
- Builder 0.1.63: 29/29 recursos del manifiesto, 1,730 entradas ZIP, chunks Host/React y extensión requeridos presentes. Setup standalone, paquete y publicación tienen el mismo SHA-256; la validación del builder compara recursos Host/UI incrustados.

Setup autónomo (`D:\TEMP\focusguard-fg166-validation\artifacts\fg214\FocusGuard-0.1.63-Setup.exe`): SHA-256 `C4C493C4D81AAA0AB497F3FF3C3B88A3FBB4176FCCE81FD394CABEB69D1E9F1A`.

ZIP menú (`D:\TEMP\focusguard-fg166-validation\artifacts\fg214\FocusGuard-0.1.63-win-x64-menu.zip`): SHA-256 `5F369EE8EA0F9817F75EA04BBE645D8EF36C5B1F1CC376CF54B0ECA3494B2F95`.

## Pendiente

No instalado. La herramienta de inspección visual de escritorio (Orca) no está en este entorno y el browser headless no produjo captura, así que la verificación de layout real sigue en Fase 9. Decisión de Block en descansos y aceptación instalada/Fases 4–10 siguen abiertas.
