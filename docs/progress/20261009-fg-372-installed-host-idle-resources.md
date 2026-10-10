# FG-372 — Muestra de recursos idle instalada .123

## Objetivo

Tomar una muestra de 60 segundos del servicio y del Host React instalados, con sesión Expired y el Host abierto.

## Resultado

- Duración: 59,997 s; 12 procesadores lógicos y 12 procesos FocusGuard/WebView2 observados.
- Service PID5732: 74,28 MiB working set, 34,72 MiB private memory y 0 % CPU.
- Árbol React Host: 7 procesos, 423,07 MiB working set y 217,29 MiB private memory.
- Total observado: 838,71 MiB working set, 460,38 MiB private memory, 0,0304 % CPU.
- Gates configurados pasan: Service bajo 100 MiB/1 % CPU, máximo un Desktop y un React Host.
- Diagnostic final: ServiceReady/ExtensionReady true, sesión Expired, Strict=false y EnforcementConnected=false. Las preferencias mantienen el SHA anterior; `FocusGuard.TestTarget` no corre.

Esta muestra solo representa reposo; no demuestra carga activa ni sostenida.

Evidencia: `artifacts/fg372-output/focusguard-resources-0.1.123-idle-60s.json`.
