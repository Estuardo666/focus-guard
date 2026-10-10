# FG-379 — horario instalado de 1 minuto en .130

## Objetivo

Aceptar en el Host real la decisión del propietario de mantener horarios de un minuto, revisar la próxima hora y comprobar un inicio y fin automáticos sin aplicar restricciones.

## Prueba y resultado

- La lista de Programación estaba vacía. En el Host instalado .130 preparé `FG378 prueba 1 min`: viernes, 14:36, zona `SA Pacific Standard Time`, perfil `2`, duración 1 minuto, bloqueo apagado y auto-inicio activado.
- La revisión mostró el horario local, el perfil, 1 minuto, estado `Activa` y «Sesión sin bloqueo». Después de guardarlo, la fila mostró Próxima sesión 14:36 y Fin programado 14:37.
- El servicio inició la sesión automáticamente. En el Host se observó una sesión normal con 00:00:43 restantes y fin confirmado a las 14:37; Servicio `Conectado`, Extensión `Disponible`.
- La sesión expiró naturalmente. Actividad registró `2`, 14:36–14:37, `Menos de 1 min`, `Sesión completada` y 0 intentos bloqueados.
- Eliminé el único horario temporal; la lista quedó vacía para impedir ejecuciones futuras. El Host volvió a la pestaña Resumen, sin sesión activa. El formulario quedó en `Ciclos de enfoque` / `Enfoque profundo`; perfil 2 y reglas no cambiaron.

## Limitación observada

El resumen de la sesión programada dice «No se sabe si el bloqueo estaba habilitado. No repetiremos con una suposición», aunque el horario era explícitamente timer-only. Es la decisión pendiente del plan: las sesiones iniciadas por horario no guardan una intención en el diario local. El comportamiento de bloqueo fue seguro: no se aplicaron restricciones.

## Evidencia

- Horario activo antes del inicio, con la próxima hora y el fin: `D:\TEMP\FocusGuard-FG378-20261009\schedule-one-minute-before-start-0.1.130.png`.
- Sesión activa con tiempo restante y canales Ready: `D:\TEMP\FocusGuard-FG378-20261009\schedule-one-minute-session-active-0.1.130.png`.
- Resultado `Iniciada`, próxima ocurrencia y fin: `D:\TEMP\FocusGuard-FG378-20261009\schedule-one-minute-last-result-started-0.1.130.png`.
- Historial con la duración subminuto y 0 intentos: `D:\TEMP\FocusGuard-FG378-20261009\schedule-one-minute-history-0.1.130.png`.
- Resumen con la limitación de intención: `D:\TEMP\FocusGuard-FG378-20261009\schedule-one-minute-session-summary-0.1.130.png`.

## UX, seguridad y siguiente paso

El control de un minuto conserva el mensaje de fin programado; el servicio empezó y expiró la sesión sin bloqueo y sin intervención manual. No se modificaron perfiles ni reglas. Continuar con los demás recorridos de Fase 8 en .130 y resolver con el propietario cómo representar sesiones programadas sin intención local.
