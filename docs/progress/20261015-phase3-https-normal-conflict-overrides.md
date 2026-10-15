# Fase 3: overrides locales durante conflicto normal HTTPS dual Host

## Resultado

La integración dual Host verifica que overrides locales de tema/idioma se mantienen en ambos dispositivos al resolver un conflicto compartido de duración. Al retirar las excepciones, cada Host aplica el tema/idioma global vigente sin publicar otra revisión.

## Evidencia

En `TwoHostSessionsConvergeOverPinnedHttpsAndPreserveLocalOverridesAcrossRestart`, owner y peer guardan overrides locales de tema/idioma antes de editar duraciones distintas (40/60) sobre el mismo snapshot. La resolución vía `HostAccountService` acepta la duración del peer. Los outboxes confirman `winningDuration/light/en`, mientras las preferencias locales permanecen `dark/es` y `light/es`, respectivamente. Limpiar ambas selecciones vuelve a `light/en` en ambos equipos sin cambiar la revisión ni el número de POST; `ReduceMotion`, `EnforcementEnabled` y `CyclePhaseNotifications` no cambian.

## Verificación

- Prueba focalizada dual Host: PASS.
- Suite `FocusGuard.Host.Windows.Tests`: **259/259 PASS**.
- Formato del test y `graphify update .`: PASS.
- Solo cobertura/documentación; `.179` no cambió, no se instaló ni se desplegó.

La aceptación física de resolución normal/offline con overrides requiere dos PCs.
