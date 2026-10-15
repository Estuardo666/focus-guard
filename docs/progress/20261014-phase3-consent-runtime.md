# Fase 3 — consentimiento, sesión y ciclo automático

## Alcance

Componer localmente el consentimiento persistido, la sesión cancelable, el controlador de reintentos con checkpoint DPAPI y el scheduler. La integración se mantiene en `cloud/sync`; no hay referencia desde el Host distribuido.

## Implementación

- `SettingsAutomaticSyncRuntime` se crea inerte; no activa consentimiento ni inicia timers/trabajo.
- `RunAsync` inicia el loop solo cuando lo llama su propietario. Si ya hay opt-in persistido, reanuda esa sesión; si no, el scheduler permanece dormido hasta una señal.
- `EnableAsync` requiere confirmación deliberada y persiste el consentimiento antes de habilitar ticks.
- `DisableAsync` baja primero la compuerta en memoria, cancela el tick activo, persiste la revocación y despierta el scheduler. Los ajustes y el borrador outbox permanecen locales.
- El tick factory recibe una compuerta activa del runtime y una lectura del consentimiento persistido; el worker debe conservar además sus comprobaciones antes de cada efecto de red/escritura.
- `DisposeAsync` cancela el loop/sesión sin borrar el opt-in guardado, para permitir reanudación tras cierre normal.

## Seguridad, recuperación y privacidad

La compuerta de sesión evita iniciar nuevos ticks tras la revocación; el `CancellationToken` detiene el trabajo en curso y el worker comprueba consentimiento persistente antes de las operaciones. Se propaga cancelación; no se transforma una cancelación externa en éxito. El coordinador no lee ni sube campos fuera del allowlist existente y no borra borradores. Un error del checkpoint sigue en `NeedsAttention(Storage)`; esta integración no añade reparación para corrupción. No se usa staging ni se accede a cuentas reales.

## Validación

- `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore`: **132/132 PASS** (la suite previa tenía 129 casos; se agregaron tres de ciclo de vida).
- `dotnet format` focalizado en los dos archivos nuevos, modo `--verify-no-changes`: PASS.
- `git diff --check`: PASS; solo avisos de conversión LF/CRLF en archivos preexistentes.
- El primer `dotnet format --verify-no-changes` de proyectos completos señaló finales de línea de los dos archivos nuevos, ya corregidos, y orden de imports en dos tests existentes ajenos al cambio. No se reformatearon esos tests.

## Límites y siguiente paso

El runtime no está conectado al Host, UI, inicio de Windows, observador real de cambios/red, cursor, membresía verificada ni API/DB reales. Por tanto no hay sincronización automática activa ni evidencia de convergencia entre dos equipos. Próximo trabajo independiente: especificar cursor/resumen remoto y política de cambios concurrentes antes de integrar en el Host. Fase 2 sigue abierta hasta verificar propietario→hermano en la PC física.