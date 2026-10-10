# FG-232 — Muestras preliminares de recursos React Host

## Método

Mismo equipo, FocusGuard Service instalado `Running` bajo `NT AUTHORITY\LocalService`, sin sesión activa y 12 procesadores lógicos. El medidor existente incluyó FocusGuard.UI y descendientes WebView2. Dos muestras secuenciales de10s, una con la copia temporal .77 (Host fuera de Program Files y sin handshake autorizado) y otra con el Host instalado .71, ambas iniciadas sin `--pilot`.

## Resultados

| Host | Árbol | Working set | Private bytes | CPU FocusGuard agregado | Gates |
| --- | ---: | ---: | ---: | ---: | --- |
| 0.1.77 temporal | 7 procesos | 376.27 MB | 194.50 MB | 0.052% | `serviceWithinBudget`, una sola Desktop y un solo React Host pasan |
| 0.1.71 instalado | 7 procesos | 394.17 MB | 201.82 MB | 0.104% | los mismos gates pasan |

El árbol temporal .77 usó alrededor de18 MB menos de working set en esta medición. No se declara una mejora de rendimiento: el .77 temporal no autenticó con el servicio instalado y no recibe el mismo estado que el Host .71; las muestras tampoco son carga sostenida.

## Artefactos y estado

- .77: `D:\TEMP\focusguard-fg166-validation\artifacts\performance\react-host-0.1.77-package-smoke.json`, SHA-256 `C43DAC4DFC793B27C0119BC62828E9B4AFECE7E0466A55010E29CEE4960ADFB5`.
- .71 repetido: `D:\TEMP\focusguard-fg166-validation\artifacts\performance\react-host-0.1.71-installed-repeat.json`, SHA-256 `10711DA82049CC9A83E4DB04C7F0F888998BBB3998DD2C45C7A92C0DF9CBFC18`.
- Los dos Hosts de QA se cerraron; FocusGuard Service quedó `Running`. No se instalaron paquetes ni se modificaron sesiones/perfiles.

La comparación funcional instalada y sostenida de .77 sigue pendiente de su Setup elevado y del smoke autenticado.
