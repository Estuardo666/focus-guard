# FG-252 — Línea base de reposo de cinco minutos

## Método
Ejecuté cinco muestras consecutivas de60s con scripts/measure-focusguard-resources.ps1 y guardé cada JSON en D:\TEMP\focusguard-fg166-validation\artifacts\fg252. El script enumera procesos y lee CPU/memoria; no envía comandos al servicio ni modifica estado. El Host React estaba cerrado. El servicio activo usó PID30976.

## Resultado
- Cinco muestras, promedio de60.00s cada una. Los gates serviceWithinBudget, singleDesktopInstance y singleReactHostInstance pasaron en las cinco.
- FocusGuard.Service: working set66.58–66.84 MB (promedio66.71), memoria privada25.79–25.97 MB (promedio25.86), CPU0.000% en las cinco.
- Todos los procesos FocusGuard: working set339.09–339.68 MB (promedio339.44), memoria privada/CPU detalladas en summary.json. CPU total varió0.033–0.059% (promedio0.046%).
- El árbol React/WebView2 estuvo ausente en las cinco muestras.

## Alcance
Amplía la muestra de reposo anterior de60s a cinco minutos, pero el Host está cerrado y no hubo sesión activa ni bloqueo real. No acredita rendimiento de la UI React, carga sostenida ni el paquete .86 sin instalar. Las muestras individuales y sus hashes están listados en summary.json; el servicio LocalService y el PID permanecieron estables.