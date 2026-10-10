# FG-245 — Replay bloqueado cuando el consentimiento legacy es desconocido

## Contrato comprobado

`SessionIntentCommands` no permite repetir una intención cuando `EnforcementEnabled` es null. Devuelve `replayable=false` con `EnforcementConsentUnknown`, conserva el valor como null y no infiere si la sesión anterior bloqueaba aplicaciones o sitios.

La cobertura del Host ahora atraviesa cuatro combinaciones: timer/ciclo y normal/estricto. Las cuatro confirman el motivo de indisponibilidad, el consentimiento nulo y que no se convierte en true o false. React muestra el aviso localizado de que no se sabe si el bloqueo estaba activo y no presenta la acción Repeat.

## Verificación

- Prueba Host parametrizada: 4 combinaciones aprobadas.
- Prueba React de historial: aviso de consentimiento desconocido visible y acción Repeat ausente.
- Suite de solución: `dotnet test FocusGuard.slnx -c Release --no-restore`, 854/854.
- Suite React: 99/99.
- El Host instalado sigue en .79. Este contrato se prueba en tests del Host/UI; no es aceptación instalada.
