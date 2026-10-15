# Fase 3 — preparación del ciclo automático de ajustes

## Alcance

Preparar con código y pruebas locales la ejecución automática del worker de ajustes, sin conectar ni iniciar el flujo desde FocusGuard Desktop. La aceptación física pendiente de Fase 2 en la otra PC Windows continúa abierta y no se sustituye por estas pruebas.

## Implementación

- `SettingsSyncScheduler` ejecuta una reconciliación al arrancar el loop explícito y continúa hasta que el propietario cancele el token.
- Un canal de capacidad uno agrupa señales concurrentes de cambios locales, conectividad y consentimiento. Evita crear una tarea por edición rápida.
- El controlador de reintentos diferencia un poll ocioso de los límites de red. Una edición local o una reconexión puede adelantar el poll ocioso; Retry-After, backoff por error transitorio y el resultado Pending/Conflict mantienen sus esperas. NeedsAttention y Disabled esperan una señal; una falla de autenticación/seguridad solo se reanuda después de `ResumeAfterRecoveryAsync`.
- El llamador recibe estados mediante el reportero opcional. Se rechaza un segundo loop concurrente para el mismo scheduler.
- El loop no activa consentimiento, no establece identidad ni alcance, y no está enlazado al Host, a la UI o al arranque del sistema. El futuro propietario debe iniciar la sesión solo después de consentimiento local persistido y conectar el ciclo de vida de cancelación con `SettingsConsentSession`.

## Pruebas y resultado

`SettingsSyncSchedulerTests` cubre señales agrupadas durante una consulta, consentimiento desactivado/reactivado, parada ante fallo crítico hasta recuperación deliberada, señal de reconexión que no elude Retry-After y rechazo de dos propietarios concurrentes. El control de reintentos sigue cubriendo cooldown monotónico, backoff, errores TLS y cancelación. La suite local dirigida terminó con 119/119 pruebas aprobadas.

La suite completa, excluyendo el proyecto headless `FocusGuard.Desktop.UiTests`, terminó con 1.116/1.116 pruebas en 15 proyectos. Los dos fallos iniciales de `Desktop.Tests` expusieron compatibilidad perdida del guardado normal con archivos vacíos/corruptos; se corrigió para permitir recuperación en un guardado local explícito, mientras que la proyección de sync sigue rechazando esos archivos sin reemplazarlos. `Desktop.Tests` quedó en 189/189 PASS.

`FocusGuard.Desktop.UiTests` se colgó, no falló una aserción: un ensayo con timeout quedó en `DisconnectedUiShowsVisibleWarningAndDisabledStopWhileServiceSessionContinues`; al excluirlo, otro quedó en `ActiveSessionShowsObjectiveAndCompactWindowKeepsStatusWhenCollapsed`. Reproducido en aislamiento y no relacionado por evidencia con el scheduler; la aceptación UI de esa suite no está validada en este turno.

`dotnet format` en modo de verificación pasó en Sync.Windows, Sync.Tests y Client. El instalador independiente .160 se compiló y validó: `scripts/test-installer.ps1`, `--package-self-test`, `--smoke-test` y `--smoke-web-ui` terminaron con exit code 0; SHA-256 `B53113B4B570CFA5BD03399BC395A9FFB8BD2850061E9667453E5305D11AC725`. Se generó en `artifacts/installer/FocusGuard-0.1.160-Setup.exe`; no se instaló. El build React/Setup reporta advertencias de tamaño de chunk y recursos de fuentes que se resuelven en runtime, existentes fuera de este cambio.

## Límites y siguiente trabajo

- En este checkpoint inicial, el cooldown/backoff sólo se conservaba en memoria. El [checkpoint DPAPI posterior](20261014-phase3-durable-retry.md) prepara persistencia opcional del backoff/Retry-After; no está conectado al Host y por sí solo no habilita runtime.
- No se añadieron cursor de cambios, observación real de red, integración con consentimiento/OIDC/membresía/API/DB ni interfaz de usuario.
- No se desplegó, no se cambió una cuenta real ni se instaló la compilación en ningún equipo.
- Los tres ensayos de hang de Avalonia dejaron seis volcados de diagnóstico (1.089.069.564 bytes, ~1,01 GiB) bajo `tests/FocusGuard.Desktop.UiTests/TestResults`. Intenté borrar exclusivamente esas carpetas creadas por las pruebas, pero la política del entorno bloqueó `Remove-Item`; quedan pendientes de limpieza manual.
- Fase 3 y Fase 2 siguen abiertas; la prueba bidireccional con la segunda PC sigue pendiente.
