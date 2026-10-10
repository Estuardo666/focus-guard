# FG-413 — decisiones estrictas del propietario y prueba de inicio estable

## Decisiones (propietario, 2026-10-10)

1. **Confirmación estricta = nombre exacto del perfil.** Se mantiene lo que ya hace el Host React. `SPEC.md` y `UX_SPEC.md` sustituyen la frase aleatoria de dos palabras en inglés por el nombre guardado del perfil (comparación ordinal), visible en la revisión y junto al aviso de trabajo sin guardar. La UI clásica de Avalonia conserva la frase solo hasta su retirada en la Fase 10. No hay cambios de código.
2. **Indicador tras una sesión estricta:** FG-410 demostró la liberación real (enforcement desconectado, snapshot eliminado y un TestTarget que ya no se bloquea). Cuando el estado es Expired o Recovered con `strictMode=true` y `enforcementConnected=false`, el Host muestra la insignia **«Estricto finalizado»**, sin el estilo de advertencia, y el texto «La sesión estricta terminó y el bloqueo se liberó. Puedes iniciar otra sesión cuando quieras.» (es/en en los locales). Durante la sesión estricta activa se sigue viendo «Estricto» con su estilo y su aviso de recuperación.

## Prueba de inicio estable

Después del reinicio de Windows, la suite React tardaba unos 28 s, frente a unos 10 s antes, y `DashboardStart.test.tsx` agotaba el timeout de 5 s en pruebas distintas en cada corrida. Una prueba atascada dejaba trabajo pendiente que rompía la siguiente (`'set value' called on an object that is not a valid instance of HTMLInputElement`). El mismo fallo se reproducía sobre el commit anterior (FG-412): era una fragilidad previa, no una regresión.

Causa: `StrictReviewPanel` (cargado con `lazy`) y `native/cycle-fixture` (importado dentro de las pruebas) se transformaban en su primer uso, y ese coste caía dentro del presupuesto de cada prueba. Corrección: un `beforeAll` precarga ambos módulos con un margen propio de 30 s. Los timeouts de cada prueba no cambian.

## Verificación

- `App.test.tsx`: prueba nueva del indicador (activo → «Estricto», con estilo; expirado → «Estricto finalizado», sin estilo, con el texto de liberación y sin el aviso de recuperación).
- React 149/149 en 4 de 4 corridas completas con la máquina cargada; `tsc` limpio.
- .NET 914/914.
- Pendiente: verlo en la app instalada (.139).
