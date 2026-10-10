# FG-281 — Aceptación instalada de FocusGuard .96

## Resultado

Se instaló el paquete acumulado `FocusGuard-0.1.96-Setup.exe` mediante el flujo elevado normal. El preflight informó que debía cerrar Host, Classic, Native Messaging y Service; el DrillClient confirmaba una sesión `Inactive`, y continué con la actualización. El Setup terminó, el Service volvió a ejecutarse y sus archivos instalados coinciden con el paquete .96.

## Verificaciones instaladas

- 18/18 archivos UI y 7/7 ejecutables principales coinciden por SHA-256 con el publish .96.
- Los cuatro smokes de `C:\Program Files\FocusGuard\FocusGuard.UI.exe` (`--smoke`, `--smoke-runtime-missing`, `--smoke-renderer-failure` y `--smoke-renderer-crash`) salieron con código 0. El Host normal PID2916 y Service PID25748 conservaron sus PID durante los smokes.
- DrillClient confirma Service Ready, extensión Ready, `Inactive`, SessionId nulo. El Service corre como `NT AUTHORITY\LocalService`.
- En Orca/WebView2 a1216×799, el carril lateral dice “Solo lectura”; el subtítulo de Perfiles describe una consulta, y el mensaje explica que editar requiere abrir el piloto o continuar en FocusGuard clásico.
- La regla guardada muestra `FocusGuard.TestTarget.exe` una vez. El detalle “Nombre del ejecutable” identifica el tipo de coincidencia y no repite el valor.
- Tras Tab, el foco visible llega a la pestaña Perfiles. No inicié ni detuve sesiones ni edité perfiles.

Captura posterior: `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/installed-profile-readonly-after-0.1.96.png`. La captura anterior .94 está en `installed-profile-readonly-before-0.1.94.png`. Recibo completo: `installed-acceptance-0.1.96.json`.

## Fase 9 — reposo .96

Una muestra idle de 60.01 segundos pasó los gates: Service66.79 MB working set, 26.00 MB private memory, 0% CPU; árbol React de 7 procesos, 438.91 MB working set y 224.40 MB private; 11 procesos FocusGuard totales, 787.14 MB working set, 445.19 MB private y 0.0716% CPU. Hubo un único Desktop Classic y un único Host React. La muestra no cubre carga activa o sostenida, Edge, reboot ni recovery. Datos: `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/focusguard-resources-0.1.96-inactive-host-60s.json`.

## Lo que permanece pendiente

La pantalla/profile acceptance concreta queda cerrada para .96. Aún faltan continuidad de Host al cerrar/reabrir durante sesión activa, aviso Activating real, forced-colors/Narrator/DPI y QA visual de las demás páginas, bloqueo real de aplicación y Edge en .96, rol incorrecto desde una ruta autorizada, rollback owner-present, correlación SCM7034, carga sostenida, reboot/recovery y gates de Fase 10.
