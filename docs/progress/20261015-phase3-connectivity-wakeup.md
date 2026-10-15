# Fase 3 — despertar automático al recuperar red

## Resultado

`MainForm` escucha `NetworkAvailabilityChanged` y `NetworkAddressChanged` durante su vida activa. Cuando Windows comunica disponibilidad/cambio de dirección, el Host despierta el scheduler si existe una sesión con consentimiento persistido. Cerrar realmente la ventana o disponerla retira ambos handlers; cerrar a la bandeja mantiene la app viva para recibir los eventos.

La señal no activa sincronización ni crea un outbox. El retry controller sigue respetando el backoff/`Retry-After` durable; si Windows detecta red disponible pero el API aún no responde, el reintento continúa con la demora normal.

## Verificación

- `HostAccountServiceDisableActionCancelsAnInFlightSyncAndPersistsRevocation`: espera dos polls Idle, envía la señal de conectividad, comprueba que se despierta un tercer `ReadAsync`, revoca consentimiento y confirma que la siguiente señal no hace I/O.
- Host: `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --verbosity minimal` — **240/240**.
- Sync: `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore --verbosity minimal` — **145/145**, incluida la prueba de que la señal no evita Retry-After.
- UI: **246/246**; `npm run check`, Vite y `scripts/verify-dist.mjs` pasan como parte del build Host.
- Formato focalizado y `graphify update .` — PASS; AST 1,570 archivos, 23,958 nodos, 162,063 aristas.
- Setup standalone `.171` pasó guard de producción, `scripts/test-installer.ps1` y `--package-self-test`. SHA-256 `5A439119DE2606B10F2B6F31A33EC6FBC90143BC1E21C1F52E534CF7983F8310`; ZIP `03C27AE288829E5B1E8DDAEE37968F9AE7ACF614AB35DEE31EBA0B554C27FE07`.

## Seguridad y límites

- El evento de Windows solo envía una señal sin contenido al scheduler existente; el scheduler solo corre con consentimiento guardado.
- Un cambio de interfaz/address no demuestra que la API responda, y no acorta `Retry-After`.
- El Setup `.171` está generado y validado localmente; no se instaló ni desplegó.
- Sigue pendiente la aceptación física entre las dos PCs y la validación de la API/base administrada de staging, que no se ejecuta antes de completar el gate físico.
