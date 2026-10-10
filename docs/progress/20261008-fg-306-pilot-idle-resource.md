# FG-306 — Baseline de reposo con Host piloto .100 abierto

Fecha: 2026-10-08
Estado: muestra de 60s aceptada; no representa carga activa.

## Escenario
Se midieron los procesos FocusGuard durante 60.01s con el Host React piloto instalado .100 abierto en su borrador existente. No se cambió duración/perfil, no se pulsó Revisar/Confirmar y no se inició sesión. Service estaba Ready/Inactive antes y después; Profile/Activity no se modificaron.

## Resultado
En un equipo de 12 procesadores lógicos:
- 11 procesos FocusGuard; exactamente un FocusGuard.UI y un Desktop.
- Host WebView tree: 7 procesos, 410.86MB working set y 205.71MB privados.
- Service PID5704: 69.02MB working set, 28.82MB privados, 0% CPU.
- CPU total de los procesos FocusGuard: 0.104% normalizado.
- Gates medidos: Service CPU<1% y WS<100MB; un Desktop; un React Host — todos pasan.

## Evidencia y límite
artifacts/fg306-pilot-draft-idle-0.1.100.json. Esta es una muestra idle con borrador abierto, no una medición de sesión activa o carga sostenida.

