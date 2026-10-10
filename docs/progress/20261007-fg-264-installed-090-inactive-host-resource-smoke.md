# FG-264 — Recursos de .90 idle con React Host abierto

## Verificación

Se ejecutó `scripts/measure-focusguard-resources.ps1 -SampleSeconds 60 -Enforce` contra la instalación .90 con el Host React abierto como usuario normal. El script sólo enumera procesos y toma deltas de CPU/memoria; no envía órdenes al Service ni cambia estado. El JSON completo quedó en `D:\TEMP\focusguard-fg166-validation\artifacts\fg265\focusguard-resources-0.1.90-inactive-host-60s.json`.

| Grupo | Procesos | Working set | Memoria privada | CPU |
| --- | ---: | ---: | ---: | ---: |
| Service PID43268 | 1 | 63.61 MB | 22.95 MB | 0% |
| Árbol React/WebView2 | 7 | 427.40 MB | 215.25 MB | — |
| FocusGuard completo observado | 11 | 760.67 MB | 425.41 MB | 0.052% |

Pasan los gates configurados: Service bajo100MB y1% CPU, máximo un Desktop y máximo un React Host. `FocusGuard.DrillClient.exe status` antes y después reportó `serviceReady=true`, `extensionReady=true`, `sessionState=Inactive`, `sessionId=null`; SCM sigue `Running` como `NT AUTHORITY\LocalService`, PID43268.

## Límites

Es una muestra de reposo de60s con Host abierto. No representa una sesión activa, enforcement, Edge, carga sostenida ni costo de la UI al cambiar pantallas.
