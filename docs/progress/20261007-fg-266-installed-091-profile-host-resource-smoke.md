# FG-266 — Recursos del Host .91 con Perfil abierto

## Verificación

Se ejecutó `scripts/measure-focusguard-resources.ps1 -SampleSeconds 60 -Enforce` después de instalar .91, con React Host en la página Profiles y sesión `Inactive`. El script sólo enumeró procesos y tomó deltas de CPU/memoria; no envió comandos al servicio.

| Grupo | Procesos | Working set | Memoria privada | CPU |
| --- | ---: | ---: | ---: | ---: |
| Service PID36440 | 1 | 66.98 MB | 26.58 MB | 0% |
| Árbol React/WebView2 | 7 | 451.29 MB | 237.46 MB | — |
| FocusGuard observado | 11 | 791.48 MB | 456.15 MB | 0.078% |

Pasan los gates: Service bajo100MB y1% CPU, máximo un Desktop y máximo un React Host. `DrillClient status` después conserva `serviceReady=true`, `extensionReady=true`, `Inactive`, sin SessionId; SCM sigue Running como `NT AUTHORITY\LocalService`, PID36440.

## Límites

Esta es una muestra de reposo de60s con la página Profiles abierta. No mide una sesión activa, enforcement, Edge, navegación bloqueada ni carga sostenida. JSON completo: `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\focusguard-resources-0.1.91-inactive-profile-host-60s.json`.
