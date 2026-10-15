# Fase 3: overrides locales, convergencia y reset en HTTPS dual Host

## Resultado

La prueba de integración HTTPS dual Host cubre prioridad, recuperación y retirada de overrides para tema, idioma y duración, en convergencia normal y durante el reset/tombstone y su conflicto offline. El propietario conserva valores locales distintos mientras el peer publica una revisión global distinta y al rebasar el reset. Tras reconstruir ambas sesiones desde DPAPI, retirar los overrides aplica la última revisión compartida sin otra publicación.

## Evidencia

En `HostSettingsSyncHttpsTests.TwoHostSessionsConvergeOverPinnedHttpsAndPreserveLocalOverridesAcrossRestart`, el propietario fija `dark/es/90` como overrides locales en el outbox DPAPI y se reinician ambas sesiones. El segundo Host recrea el documento global como `light/en/35`; el outbox del propietario recibe la nueva revisión y conserva efectivos `dark/es/90`. Al retirar los overrides, se aplican `light/en/35` sin cambiar revisión, borrador, estado pendiente o número de POST. Luego se vuelven a fijar los tres overrides y el propietario solicita reset; el peer publica idioma nuevo, causando conflicto con el tombstone. La rebase publica la revisión de reset, que ambos Hosts confirman: el propietario conserva `dark/es/90` y el peer aplica `GlobalSettings.Default`. Retirar las excepciones proyecta esos defaults sin otra publicación. `ReduceMotion`, `EnforcementEnabled` y `CyclePhaseNotifications` siguen locales. El documento compartido permanece cifrado y el objeto de red no incluye `LocalOverrides`.

La primera ejecución de reset encontró que el test señalaba al peer antes de confirmar la publicación del propietario; el peer podía leer la revisión previa y volver al intervalo programado. El ensayo ahora despierta al peer después de la revisión confirmada. Además, espera la proyección `DesktopSettings` después de la confirmación del outbox, porque el tick completa ambos pasos en operaciones separadas.

## Verificación

- Prueba focalizada: **1/1 PASS**.
- Suite `FocusGuard.Host.Windows.Tests`: **259/259 PASS**.
- Formato de `HostSettingsSyncHttpsTests.cs`: PASS.
- `graphify update .`: PASS.
- `git diff --check`: PASS.

Es un cambio de pruebas/documentación; no requiere regenerar el instalador y no se instaló ni desplegó nada. El candidato distribuible sigue siendo `.179`. No equivale a aceptación física ni a validación de staging.

## Seguridad, recuperación y gates restantes

Los overrides permanecen en el outbox DPAPI scoped por cuenta/dispositivo y nunca se publican como estado global. Quitar las excepciones no edita el documento compartido; deja que la proyección use la revisión global ya confirmada. El código distribuible no cambió y `.179` sigue siendo el candidato. El ensayo físico de tombstone offline, override/importación en el peer y preservación de todas las reglas/permisos sigue pendiente de la segunda PC.
