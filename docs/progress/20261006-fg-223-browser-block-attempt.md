# FG-223 — Intento de bloqueo web instalado en browser temporal

## Plan

- Crear una regla Block para un dominio `.invalid` en un perfil temporal, sin alterar perfiles existentes ni bloquear sitios reales.
- Cargar la extensión oficial 0.1.71 con su clave estable para que el Native Messaging origin coincida.
- Verificar la navegación y el bloqueo sólo si el navegador de prueba puede iniciarse por una vía permitida.
- Eliminar el perfil temporal y registrar cualquier restricción de ejecución sin buscar rutas alternativas para eludirla.

## Resultado

Se creó `FG Browser QA 20261006` con una regla web Block `focusguard-test.invalid`; no se añadieron reglas de aplicaciones ni horarios. Se preparó y revisó un timer normal de un minuto con enforcement en el Host `--pilot`. El servicio aceptó el timer y la extensión instalada seguía Ready.

Se intentó iniciar Chrome con un perfil temporal aislado, la extensión staging oficial 0.1.71 y un puerto local de DevTools para consultar su URL sin interactuar con el perfil personal. El executor rechazó el proceso antes de crearlo (`CreateProcess ... rejected by policy`). No se navegó ni se verificó el bloqueo web; no se probaron flags alternativos ni se abrió la Chrome existente.

La sesión expiró sin navegar. Se eliminó el perfil temporal FocusGuard desde la UI. La lista volvió a los cinco perfiles originales; `browser-profile-test` no se creó. No se modificaron perfiles existentes ni horarios. Quedó un tercer registro de sesión de prueba en Activity.

## Verificación de componente

- En `extension`: `npm test` pasó 19/19; `npm run check` (`tsc --noEmit`) pasó. Estas pruebas cubren el motor, el allow precedence, navegación interna/fail-open, policy hashes y rendimiento, pero no sustituyen una navegación en Chrome con la extensión instalada.

## Estado externo y seguridad

- Servicio `LocalService`: Ready; extensión: Ready; sesión final: Expired.
- Desktop y ProcessAgent siguen presentes en SessionId 1.
- Perfil/browser temporal limpiado; no queda ventana Host React abierta.
- Este intento no es una prueba de browser blocking. La política de ejecución impide la vía de browser aislado probada; no se busca eludirla.

## Pendiente

Reanudar la aceptación web cuando exista una forma autorizada de controlar una instancia de navegador aislada. Usar sólo el dominio `.invalid`, comprobar la redirección/página bloqueada y cerrar el timer. Mantener Fase7 browser y fases 4–10 abiertas hasta tener evidencia real.
