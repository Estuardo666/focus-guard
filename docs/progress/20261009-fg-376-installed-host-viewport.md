# FG-376 — ventana del Host a 1025×701 en .123

## Objetivo

Comprobar en el Host instalado si la vista sigue utilizable en una ventana más baja y ancha que el tamaño de escritorio habitual, sin iniciar la sesión que aparecía como borrador.

## Prueba y resultado

- El Host instalado era `FocusGuard.UI` PID 16504, como propietario y con la ventana en pantalla. Tamaño inicial: 1216×799.
- Redimensioné temporalmente la ventana a 1025×701. La vista principal se estrechó sin recorte horizontal. Parte inferior de la tarjeta quedó fuera del primer viewport, con scrollbar vertical visible.
- Desplacé la página y confirmé visualmente que `Confirmar e iniciar` era alcanzable. No pulsé el botón.
- Restauré el tamaño original 1216×799 y confirmé el tamaño por una nueva enumeración de ventanas.
- El Host mostraba Servicio `Conectado`, extensión `Disponible` y `Sin sesión activa`. No cambié reglas, preferencias ni estado del servicio.

## Observación pendiente

El borrador visible indicaba `Revisión válida hasta 9 oct 2026, 1:27:27 p. m.`; la lectura del Host ya era de las 13:30. No inicié esa revisión. Hace falta reproducir por separado si el Host conserva un borrador vencido y qué ocurre al intentar revisarlo; esta prueba no determina la causa ni lo registra como defecto confirmado.

## Evidencia

Capturas del Host instalado guardadas fuera del repositorio para no añadir binarios de QA a E:

- `D:\TEMP\FocusGuard-FG376-viewport-validation-20261009\viewport-1025x701-top.png`
- `D:\TEMP\FocusGuard-FG376-viewport-validation-20261009\viewport-1025x701-scrolled.png`
- `D:\TEMP\FocusGuard-FG376-viewport-validation-20261009\viewport-restored-1216x799.png`

## Alcance y siguiente paso

Es solo una observación instalada de .123 a 1025×701. No cubre .128, 1024×700 exactos, 800×600, 720×480, escala DPI, Narrator, forced-colors ni Windows 11. Repetir en .128 con la matriz de viewports del plan y reproducir el borrador vencido sin confirmar una sesión accidentalmente.

No hubo cambios de código; no se regeneró instalador.
