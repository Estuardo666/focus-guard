# FG-310 — Recursos idle del Host instalado .102

Fecha: 2026-10-08
Estado: muestra idle instalada completada; no representa carga activa.

## Medición

Se ejecutó scripts/measure-focusguard-resources.ps1 -SampleSeconds 60 -Enforce en la instalación .102 con FocusGuard.Desktop y FocusGuard.UI abiertos, sesión inactiva y servicio LocalService. La captura duró 60.015 segundos con 12 procesadores lógicos.

- Service PID25096: 73.04MB working set, 33.22MB private, 0% CPU; límite <100MB/<1% aprobado.
- Un Desktop y un React Host; ambos límites aprobados.
- React Host tree: 8 procesos, 490.35MB working set y 240.27MB private.
- 11 procesos totales, 838.28MB working set, 483.52MB private, 0.0846% CPU agregado.

No se inició ni detuvo una sesión. El agente y los dos hosts estaban en token medium; el servicio siguió Running como LocalService. Esta muestra idle no sustituye carga activa/sostenida, medición de impacto de bloqueo, ni ciclo/reinicio/recuperación.

Evidencia JSON: artifacts/fg305-output/fg310-idle-resource-0.1.102-60s.json.
