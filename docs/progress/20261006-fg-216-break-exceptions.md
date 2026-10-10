# FG-216 — Excepciones Block-only durante descansos

## Resultado

Storage guarda las reglas de excepción de apps/sitios para descanso como `Block`; el editor clásico sólo crea bloqueos, y la copia en React dice que esos destinos permanecen bloqueados. El contrato de preparación, sin embargo, aceptaba únicamente `Allow`, y los flujos normal/strict rechazaban los `Block` que ya existían. Eso impedía revisar e iniciar un ciclo React con esos perfiles.

Se unificó la validación en `CycleIpcProtocol`, `SessionWorkflow` y `StrictSessionCommands`: los sets de descanso sólo admiten `Block`. Durante un descanso con autorización vigente se usa el set de excepciones y se liberan las reglas de foco ordinarias; al límite autorizado vuelven las reglas de foco. La revisión normal de React ahora muestra nombres en una divulgación accesible y la strict conserva sus listas explícitas; las listas se limitan a 12 rótulos de 512 caracteres y avisan si se truncaron. No se convirtió, borró ni reescribió configuración persistida. El fundamento queda en [ADR 0014](../adr/0014-block-only-break-exceptions.md).

## Verificación

- Contract tests: 60/60, incluida codificación/decodificación exacta de las reglas Block.
- Client tests: 109/109, incluida una regresión con SQLite y Named Pipe que prepara/activa el ciclo, cruza a descanso, comprueba las excepciones en la política distribuida y verifica que el perfil siga intacto.
- Host tests: 139/139, incluidos review strict sin activar y preservación de nombres de excepciones.
- React: 16 archivos, 76 pruebas aprobadas. TypeScript, Vite, CSP y recursos locales pasan en el build de producción.
- `dotnet build FocusGuard.slnx -c Release --no-restore` — 0 warnings, 0 errores.
- El test completo de la solución terminó con otros tres fallos: dos pruebas de edición de presets en `FocusViewModelTests` y `ColdIdentityTests` no encuentra el artefacto `ColdIdentityProbe`. No dependen de estos cambios y no se modificaron.
- `graphify update .` — 9,972 nodos, 25,367 aristas, 977 comunidades.
- Package 0.1.65 generado y verificado: 29/29 recursos del manifest, 0 ausentes, 1,730 entradas ZIP. SHA-256 idéntico en las tres copias del Setup: `80406F19304E3F40371DBCF9252470BE0A205F79575F0495B8244F5AA5A6D566`; menú ZIP: `B363AB53371B281B4A8C241A9EF3C6B1D1E3AF89725A5061A9DDBB8E0DA48A3F`. No instalado.

## Pendiente

La aceptación de la política en instalación real y la captura visual del host siguen dentro de Fase9. La decisión de compatibilidad de reglas no certifica el bloqueo real ni la recuperación con LocalService.
