# FG-346 — Transición de ciclo y parada desde React

**Fecha:** 2026-10-08 local / 2026-10-09 UTC  
**Fase:** 8, sesiones y ciclos  
**Estado:** transición Focus → descanso corto y parada normal aceptadas en el Host .112.

## Verificación

Con el servicio detenido, Service y Extension listos y Strict apagado, React presentó una revisión válida de un ciclo personalizado de 2 minutos: 1 minuto de enfoque seguido de 1 minuto de descanso corto. La configuración se revisó antes de confirmar.

El estado activo informó el mismo `SessionId` (`6cc6faf4-1d4f-45b6-b465-71d4ac5f804b`), perfil seleccionado, hora final exacta y fase actual `Focus`, seguida de `ShortBreak`. Una lectura posterior confirmó la transición a `ShortBreak` con 60 segundos restantes. La sesión se detuvo desde React durante el descanso corto; el estado final fue `Stopped`, con Service y Extension listos, Strict apagado y `EnforcementConnected=false`.

## Alcance y límites

Esto valida revisión, inicio, transición entre dos fases, presentación del estado y parada normal para un ciclo corto. No valida el descanso largo, varios ciclos, salto de fase, finalización natural, bloqueo real de sitios o aplicaciones durante las fases, ni strict/recovery. La sesión se configuró sin enforcement y no afectó procesos ni navegación.

El ensayo dejó el modo de ciclo como preferencia de escritorio. En la siguiente apertura, Preferencias .112 mostró el error de guardado y no ofreció controles. Al pulsar «Actualizar» cargó las preferencias; se restauró `SimpleTimer` desde la UI y la lectura de `desktop-settings.json` confirmó `lastSessionMode=0`. El estado guardado de Service/Extension seguía listo y no se inició una sesión.

La reproducción permitió localizar el fallo de Preferencias: durante el arranque, el dashboard inicia `profiles.list` y React pide las preferencias mientras el cliente aún tiene una solicitud pendiente. El bridge devuelve `RequestInProgress`; `loadPreferences()` no lo reintentaba y la UI traducía el error de lectura como un error de guardado. Se añadió un reintento acotado y una regresión de arranque en `src/FocusGuard.UI/src/native/dashboard.ts` y `dashboard.test.ts`; React terminó con 122/122 pruebas y el build pasó. El arreglo ya está empaquetado en Setup .118; aceptación instalada pendiente. Detalle: `docs/progress/20261008-fg-347-preferences-startup-retry.md`.

## Evidencia

- `artifacts/fg346-cycle-review.json`: revisión previa a confirmar.
- `artifacts/fg346-output/fg346-cycle-current.json`: fase Focus activa.
- `artifacts/fg346-output/fg346-cycle-transition.json`: transición observada a ShortBreak.
- `artifacts/fg346-output/fg346-cycle-stop.json`: parada normal y estado final.
