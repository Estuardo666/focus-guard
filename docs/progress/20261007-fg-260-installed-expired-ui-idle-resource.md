# FG-260 — Recursos con Host instalado abierto y sesión expirada

## Plan

Tomar una muestra de 60 segundos del árbol FocusGuard instalado mientras la sesión estuviera expirada y el Host React permaneciera abierto. No enviar comandos de sesión ni cambiar preferencias.

## Estado y resultado

La última consulta read-only de DrillClient devolvió `serviceReady=true`, `extensionReady=true`, `sessionState=Expired`, `remainingSeconds=0`, `enforcementConnected=false` para SessionId `4b203b6b-f6c4-4554-a6fb-4f8153945330`. El servicio instalado corrió como `LocalService`, PID30976; Host .86 PID32000 y Desktop PID20620 continuaron abiertos.

Durante60.0078s el script encontró11 procesos: 7 en el árbol React/WebView2. Service usó71.01MB de working set,28.20MB de memoria privada y0%CPU. El árbol React usó460.53MB WS/232.78MB privada; Desktop y Host fueron únicos; el total FocusGuard consumió0.098%CPU. Todos los gates del script pasan: Service <100MB/<1%CPU, un Desktop y un Host.

JSON: `D:\TEMP\focusguard-fg166-validation\artifacts\fg260\focusguard-resources-0.1.86-expired-host-60s.json`.

## Límites y siguiente paso

Es una muestra de reposo con ventana abierta en .86. No mide bloqueo activo, carga sostenida, pico al cambiar fases ni memoria de .89. No envié Activate/Stop ni reinicié el servicio. FG-242 sigue pendiente de instalar .89 con owner presente y repetir aceptación visual/teclado; Fase9 aún necesita carga activa, recuperación y rollback.
