# FG-398 — rendimiento activo con enforcement en .135

## Resultado (2026-10-09)

En el Host .135 instalado confirmé una sesión normal de 3 min para el perfil `gpt test`, enforcement ON y Strict OFF. El único bloqueo de aplicación es `FocusGuard.TestTarget.exe`; la única regla web permite `focusguard.invalid` (0 sitios bloqueados). El TestTarget PID 24936, con límite propio de 240s, tuvo su último heartbeat a las 23:06:58 (unos 15s después de iniciar); al finalizar la muestra de 60s ya no seguía activo. La tarjeta activa mostró Servicio Conectado, Extensión Disponible y Protección «Confirmada». Actividad informó 1 intento bloqueado; la sesión expiró por sí sola como `Expired`.

Durante 60.006s de enforcement activo, el árbol React tuvo 7 procesos, 438.9 MiB WS y 209.7 MiB privados; los 12 procesos FocusGuard sumaron 866.4 MiB WS, 463.5 MiB privados y 0.516% CPU. El servicio usó 78.6 MiB WS y 0% CPU. Los tres gates del medidor pasaron: presupuesto del servicio, una instancia Desktop como máximo y una instancia Host React como máximo. El proceso Classic Desktop (PID 27292) permaneció abierto durante la muestra; no se modificó.

No abrí sitios web ni detuve la sesión manualmente. Restauré `desktop-settings.json` byte por byte a SHA `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55` y LastWrite 13:19. La DB conservó 5 perfiles, 5 reglas de app y 30 sitios.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg398-installed-active-enforcement-resources-135.json` y `fg398-active-resources-60s.json`.

Captura visual del resumen instalado a 1216×799, con 1 intento bloqueado y 3 min completados: D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg399-installed-activity-summary-screen.png.
