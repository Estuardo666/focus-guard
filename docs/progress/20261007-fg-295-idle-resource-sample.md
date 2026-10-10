# FG-295 — Muestra de rendimiento idle de la instalación .100

Fecha: 2026-10-07
Estado: muestra de 60s aceptada; carga activa/sostenida pendiente.

## Escenario y captura

Se midió la instalación FocusGuard .100 con servicio en estado Expired, sin sesión activa. FocusGuard Service PID46424 estaba Running como LocalService; serviceReady y extensionReady permanecieron true. En el escenario había un Host React normal PID25072, FocusGuard Desktop PID39216 y ProcessAgent PID31896. El Host piloto de prueba ya estaba cerrado.

Se tomaron siete muestras cada 10 segundos de CPU y Private Working Set con Windows performance counters. La captura cubre aproximadamente 60 segundos. No se inició sesión ni se tocó el perfil.

## Resultado

En una máquina con 12 procesadores lógicos:

- Service: Private Working Set promedio/pico 24.46MB; Private Bytes al cierre 28.69MB. CPU promedió 0.98% de un lógico, equivalente a 0.082% del equipo; pico equivalente 0.104%.
- ProcessAgent: Private Working Set promedio 20.51MB; CPU media 0.033% del equipo.
- React normal: Private Working Set promedio 28.72MB; CPU media 0.028% del equipo.
- Desktop: Private Working Set promedio 110.46MB; CPU media 0.032% del equipo.

El gate idle del Service de menos de 100MB y menos de 1% CPU pasa holgadamente. El equipo mantuvo un Desktop y un React normal. Estos datos no miden una sesión activa prolongada ni presión de concurrencia.

## Verificaciones y evidencia

El servicio estaba Expired antes y después de la muestra, sin cambios en ServiceReady/ExtensionReady. Registro completo con las 56 lecturas: D:\TEMP\focusguard-fg166-validation\artifacts\fg290\fg295-idle-resource-0.1.100.json.

No se cambió código, por lo que no se recompilaron instalador ni suites automáticas. Faltan carga activa/sostenida, reinicio/recuperación, Edge, DPI/forced-colors y gates de Fase10.
