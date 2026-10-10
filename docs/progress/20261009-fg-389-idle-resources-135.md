# FG-389 — muestra de reposo instalada .135

El 2026-10-09 medí 60.016 s en la instalación .135 reparada, con el Host React abierto en Summary y sin sesión activa. Service/Extension estaban Ready, Strict OFF, `enforcementConnected=false` y `FocusGuard.TestTarget` cerrado.

- Service: 69.17 MiB working set, 27.83 MiB privado, 0% CPU.
- Árbol React: 7 procesos, 446.17 MiB working set y 230.84 MiB privados.
- Total: 12 procesos, 809.87 MiB working set, 425.36 MiB privados y 0.0673% CPU agregado.
- Los gates de presupuesto de servicio, una instancia Desktop y una instancia React Host pasan. Estado/ preferencias no cambiaron; el SHA de `desktop-settings.json` se conserva.

Es solo un baseline idle, no valida carga activa/sostenida. Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\focusguard-0.1.135-idle-60s.json` y `idle-postrepair-state.json`.