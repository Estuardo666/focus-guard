# Fase 3 — integración del Host y revisión de conflictos

## Plan de este incremento

La auditoría encontró que el cableado inicial ya existe en `HostAccountService`, el puente nativo y la UI, pero el backlog aún lo describía como pendiente y faltaba una forma de resolver conflictos reales. Este incremento verifica el cierre del ciclo Host y completa la revisión/elección por campo.

1. Añadir una prueba Host con transporte bloqueado que invoque `DisableSettingsSync`, compruebe cancelación del tick, revocación DPAPI y estado visible `Disabled`.
2. Exponer los estados del runtime y solo los tres valores de ajustes en conflicto.
3. Resolver desde UI enviando revisión y origen elegido; el Host obtiene los valores del outbox vigente y rechaza stale, campos ajenos y tombstones.
4. Verificar localmente Host, Sync, UI, bridge y el build/verificador de renderer.
5. Actualizar `TASKS.md` y el plan. No llamar staging ni instalar.

## Resultado

- El Host observa los resultados del scheduler y refleja consentimiento, espera, conflicto y atención; la página no envía valores de preferencias al resolver.
- La revisión muestra los valores actuales del documento cifrado y el borrador local por cada campo en conflicto. La elección se guarda y proyecta antes de despertar el runtime. El idioma, duración y opciones locales no disputadas se preservan.
- El puente acepta solo `revision` y una lista acotada de pares campo/origen; valida nombres, unicidad y límites. El Host vuelve a comprobar revisión, máscara y valores revisados contra el outbox DPAPI actual antes de aplicar.
- Los conflictos que incluyen reset/tombstone se anuncian para atención y se rechazan hasta tener recuperación dedicada; no se convierte una eliminación en ajustes por defecto ni se recrea contenido automáticamente.
- La revocación desde la acción Host cancela un `ReadAsync` en curso, persiste consentimiento `false` y muestra `Disabled` antes de resolver la acción.

No se llamó staging, no se publicó ni instaló una versión. La limpieza de los cuatro artefactos pequeños `.160`–`.163` permanece separada; D: tiene más de 100 GiB libres y no limita este incremento.

## Seguridad y recuperación

- El estado inicial de consentimiento procede de un vault/outbox DPAPI temporal de prueba.
- La solicitud bloqueada se cancela por el propio runtime al desactivar; no se altera la API ni el estado de ninguna cuenta real.
- La prueba no toca sesiones, perfiles, reglas, permisos ni datos personales.
- El puente no acepta `theme`, `language`, duración ni otros valores propuestos; solo permite elegir entre el outbox local y el valor remoto ya autenticado y descifrado.
- Persistir y proyectar la resolución usa el servicio de conflicto existente, que compara de nuevo revisión, máscara y draft; ante cambio concurrente falla sin reemplazar preferencias.

## Verificación

- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj --no-restore --verbosity minimal` — **234/234**.
- `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj --no-build --no-restore --verbosity minimal` — **139/139**.
- `npm run check` — PASS; `npm test -- --run` — **240/240**.
- Build de renderer y `scripts/verify-dist.mjs` — PASS como parte de la compilación Host; conserva las verificaciones de contraste, forced-colors y layout.
- `scripts/test-installer.ps1` — PASS, incluidas las pruebas de copia de extensión instalada y readiness retries.
- `scripts/build-installer.ps1 -Version 0.1.167` con `OutputDirectory=artifacts/installer/phase3-final/FocusGuard-0.1.167`, extensión aislada en `artifacts/browser-extension/phase3-final` y sufijos `phase3-final` — PASS. `Assert-FocusGuardProductionClient.ps1` verifica el endpoint en los dos assemblies del Client.
- El setup del paquete y el ejecutable independiente `artifacts/installer/phase3-final/FocusGuard-0.1.167-Setup.exe` tienen el mismo SHA-256: `F327B8A4E51E7B7413D65AABAD5DC65CED7D217D7011CC82FD43583D1D89ABB3`; `--package-self-test` devuelve exit code 0.

La aceptación física de sincronización automática de dos Windows sigue pendiente. Fase 3 no está cerrada; no se contactó staging ni se reemplazó ninguna instalación. La recuperación de reset/tombstone se añadió después; ver [checkpoint siguiente](20261015-phase3-reset-conflict-plan.md).
