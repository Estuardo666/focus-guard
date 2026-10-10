# FG-332 — Muestra idle de FocusGuard 0.1.112

**Fecha:** 2026-10-08  
**Fase:** 9  
**Estado:** Muestra idle aceptada; carga activa/sostenida pendiente.

Se ejecutó `scripts/measure-focusguard-resources.ps1 -SampleSeconds 60 -Enforce` con Desktop y el Host React .112 abiertos, servicio LocalService, estado `Inactive`, `serviceReady=true`, `extensionReady=true` y Strict desactivado. La captura duró 60.009 s en un equipo con 12 procesadores lógicos.

## Resultado

- Service PID 760: 67.61 MB working set, 27.49 MB memoria privada, 0% CPU. El gate de menos de 100 MB y menos de 1% CPU pasa.
- Árbol del Host React: 7 procesos, 416.52 MB working set, 208.64 MB memoria privada; una sola instancia del Host.
- Conjunto FocusGuard: 11 procesos, 0.0911% CPU del equipo, 758.80 MB working set y 427.07 MB memoria privada. Los gates de una instancia de Desktop y Host pasan.
- Frente a la muestra idle de .102 (FG-310), Service baja 5.43 MB en working set y 5.73 MB en memoria privada; el conjunto baja 79.48 MB working set y 56.45 MB privados. La serie es idle y observacional; no mide el coste de una sesión activa.

El estado volvió a confirmarse `Inactive`, con servicio y extensión listos y Strict desactivado. No se inició una sesión.

Evidencia: `artifacts/fg331-output/focusguard-0.1.112-idle-60s.json`; muestra anterior: `artifacts/fg305-output/fg310-idle-resource-0.1.102-60s.json`.
