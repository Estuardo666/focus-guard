# Fase 3: precedencia de override local en HTTPS dual Host

## Resultado

La prueba de integración HTTPS dual Host ahora cubre la prioridad de un override de dispositivo sobre un cambio compartido, la recuperación del override al reconstruir las dos sesiones desde DPAPI y su retirada sin una publicación adicional. El código distribuible no cambió; la cobertura complementa la aceptación física que continúa pendiente.

## Evidencia

En `HostSettingsSyncHttpsTests.TwoHostSessionsConvergeOverPinnedHttpsAndPreserveLocalOverridesAcrossRestart`, el propietario fija un tema local en el outbox DPAPI y se reinicia. El segundo Host recrea el documento global con otro tema; el outbox del propietario recibe la nueva revisión y su interfaz conserva el override. Al retirarlo, se aplica el último tema global y se comprueba que no cambian revisión, borrador, estado pendiente ni número de POST. `ReduceMotion`, `EnforcementEnabled` y `CyclePhaseNotifications` siguen locales. El documento compartido permanece cifrado y el objeto de red no incluye `LocalOverrides`.

La primera ejecución reveló una carrera en la observación del test: `SettingsSyncDesktopTick` confirma el outbox y después proyecta `DesktopSettings` bajo una operación separada. Un lector concurrente puede ver la nueva revisión brevemente antes del archivo de preferencias. El producto mantuvo la proyección; la integración ahora espera la convergencia observable de ambos almacenes en vez de suponer que la lectura del outbox ya completó el paso de proyección.

## Verificación

- Prueba focalizada: **1/1 PASS**.
- Suite `FocusGuard.Host.Windows.Tests`: **259/259 PASS**.
- Formato de `HostSettingsSyncHttpsTests.cs`: PASS.
- `graphify update .`: PASS.
- `git diff --check`: PASS.

Es un cambio de pruebas/documentación; no requiere regenerar el instalador y no se instaló ni desplegó nada. El candidato distribuible sigue siendo `.179`. No equivale a aceptación física ni a validación de staging.

## Seguridad, recuperación y gates restantes

Los overrides permanecen en el outbox DPAPI scoped por cuenta/dispositivo y nunca se publican como estado global. Quitar la excepción no edita el documento compartido; deja que la proyección use la revisión global ya confirmada. El ensayo físico de tombstone offline, override/importación en el peer y preservación de todas las reglas/permisos sigue pendiente de la segunda PC.
