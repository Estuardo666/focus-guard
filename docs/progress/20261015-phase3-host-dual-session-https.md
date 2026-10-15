# Fase 3 — convergencia HTTPS con dos sesiones Host

## Resultado

Se unieron las dos piezas que antes se probaban por separado: el transporte HTTPS/E2EE de dos dispositivos y la composición Windows `HostSettingsSyncSession`. La prueba crea dos identidades distintas bajo una cadena de membresía firmada de dos revisiones, guarda cada identidad y contenido compartido en un vault DPAPI independiente, y prepara archivos de preferencias/outbox separados.

Un servidor Kestrel HTTPS de loopback usa un certificado fijado por SHA-256. El middleware acepta solo bearer y sesión sintéticos asociados al DeviceId, prueba de posesión firmada fresca y request id anti-replay. El endpoint comprueba firma del encabezado cifrado y CAS de revisión, y solo guarda el documento E2EE.

Ambos Hosts aceptan explícitamente la sincronización y arrancan el scheduler. Una edición seleccionada de tema en el primer Host queda persistida, se publica automáticamente al despertar el scheduler y aparece en el outbox y en `desktop-settings.json` del segundo Host después de la señal de reconexión. Los ajustes locales difieren a propósito (`ReduceMotion` y notificaciones); se mantienen distintos en los dos dispositivos.

Después se forzó un conflicto CAS real: ambos outboxes guardaron duraciones distintas y el servidor dejó que ambos leyeran primero la misma revisión; una escritura ganó y la otra quedó en conflicto. La revisión y elección se ejecutaron mediante la acción `ResolveSettingsSyncConflict` de `HostAccountService`, con «otro dispositivo» como origen; el dispositivo en conflicto limpió su draft proyectando la duración ganadora y mantuvo su consentimiento. El test confirma que ambos Hosts terminan con la misma revisión/valor y que los campos locales no compartidos siguen distintos.

La prueba integrada completa pasó **3 ejecuciones consecutivas**. El orden de la señal de reconexión se ajustó para que el primer tema se confirme antes de despertar al segundo; la barrera de dos GET fija la revisión base del conflicto. Estas pruebas deterministas no modificaron la implementación de producto.

## Verificación

- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --verbosity minimal` — **239/239**.
- `TwoHostSessionsConvergeOverPinnedHttpsAndPreserveDeviceLocalPreferences` — 3/3 ejecuciones verdes.
- El build Host pasó `npm run check`, Vite production build y `scripts/verify-dist.mjs`.
- `dotnet format whitespace FocusGuard.slnx --verify-no-changes --no-restore --include tests/FocusGuard.Host.Windows.Tests/HostSettingsSyncHttpsTests.cs tests/FocusGuard.Host.Windows.Tests/HostSettingsSyncSessionTests.cs cloud/sync/Windows/SettingsRetryController.cs cloud/sync/Windows/SettingsSyncScheduler.cs cloud/sync/Tests/SettingsRetryCheckpointStoreTests.cs cloud/sync/Tests/SettingsSyncSchedulerTests.cs src/FocusGuard.Host.Windows/Hosting/HostAccountService.cs` — exit 0.
- `graphify update .` — AST 1,514 archivos; 23,832 nodos, 159,204 aristas.
- La integración usa el certificado y bearer de prueba creados dentro del test; no se llamó API real, staging ni se usaron credenciales personales.

## Alcance y límites

- Esta cobertura es más fuerte que dos runtimes aislados: atraviesa validación de vault, historial de membresía, outbox y preferencias DPAPI, transporte HTTP firmado/TLS, E2EE, scheduler y servicio de resolución de conflicto del Host en dos identidades.
- El servidor de prueba es Kestrel de loopback en la misma PC. No prueba LAN/WAN, Cloud Run, Keycloak real, instalación en las dos computadoras, cierre/reinicio del sistema operativo ni aceptación física del propietario.
- La matriz física de edición, reinicio del proceso/sistema, reconexión y conflictos sigue abierta.
- Solo se añadieron dependencias y código de prueba. El Setup `.169` no cambió y no necesita regeneración por este incremento. No se instaló, desplegó ni borró ningún archivo del producto.

## Siguiente paso

Seguir cerrando cobertura local sobre reinicio/reconexión y conflictos a través de la composición Host. La aceptación de dos Windows físicos sigue siendo un gate separado cuando el hardware esté disponible.
