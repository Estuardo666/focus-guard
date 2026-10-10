# FG-277 — Fase 9: recursos del Host .94 instalado en reposo

## Resumen

Medí durante 60.02 s el Host React .94 instalado como usuario normal, con Agent hijo y Service en Ready/Inactive. La sesión permaneció inactiva sin SessionId durante toda la muestra.

## Resultado

- Service PID18792: 67.13 MB working set, 26.15 MB private memory, 0% CPU.
- Árbol React/WebView2/Agent: 8 procesos, 480.02 MB working set y 248.83 MB private.
- FocusGuard total: 10 procesos, 593.71 MB working set, 292.97 MB private y 0.065% CPU.
- Pasaron los gates configurados: Service menor de100 MB y1% CPU; máximo un Desktop y un React Host.
- DrillClient confirmó Ready/Inactive antes y después; Service permaneció Running con el mismo PID.

## Comando y artefacto

scripts/measure-focusguard-resources.ps1 -SampleSeconds 60 -Enforce -OutputPath D:/TEMP/focusguard-fg166-validation/artifacts/fg276/focusguard-resources-0.1.94-inactive-host-60s.json

El script sólo enumera procesos y mide deltas de CPU/memoria. No envía órdenes al Service, no abre/cierra ventanas y no cambia reglas.

## UX, seguridad y límites

No se modificó UI ni código, no se inició ni detuvo una sesión y no se lanzó TestTarget. Esta muestra caracteriza un idle con UI visible; no demuestra coste durante una sesión activa, carga sostenida, otro DPI, Edge ni reinicio/recovery.

## Siguiente tarea

Completar aceptación visual instalada .94 y una muestra bajo sesión activa normal antes de cerrar los gates de rendimiento de Fase9. Rollback owner-present permanece pendiente en contexto elevado.