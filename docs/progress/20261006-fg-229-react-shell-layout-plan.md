# FG-229 — Corrección de jerarquía full/compact del Host React

## Evidencia

La captura de `FocusGuard.UI.exe` 0.1.75, arrancado sin elevación y sin argumentos desde `D:\TEMP\focusguard-fg166-validation\artifacts\fg227\publish\FocusGuard.Host.Windows`, muestra el dashboard completo y simultáneamente la cabecera/acciones de `CompactFocus`. Varios controles quedan pegados; no hay composición visible para la ventana compacta de 480 DIP. La captura se preservó en `D:\TEMP\focusguard-fg166-validation\artifacts\fg227\react-host-package-smoke.png`.

El Host ejecutado desde la ruta de artefactos no está instalado ni autorizado como cliente de servicio, por lo que su estado desconectado es esperado y no sirve como aceptación de IPC ni del servicio instalado.

## Plan

1. Renderizar `CompactFocus` oculto semánticamente salvo en modo compacto, con regresión de React para ambos modos.
2. Dar estilo de acciones del encabezado full y componer la tarjeta compacta con identidad, acciones, tiempo, perfil, detalles y avisos legibles a 480×340 y 480×220 DIP.
3. Mantener límites del piloto: sin cambio de IPC, sesiones, datos ni bandeja; respetar Geist, graphite/mint, controles de 44 px, warnings visibles y reduced motion.
4. Correr tests/typecheck/build React, Release solution, validación de instalador y Graphify; crear un paquete 0.1.76 aislado en `D:\TEMP` sin instalarlo.
5. Reabrir el Host 0.1.76 publicado desde su ruta temporal en modo lectura, tomar capturas de full y compact, revisar el layout y cerrar únicamente los PIDs iniciados para QA.

## Aceptación

- La vista full no muestra ni expone controles de `CompactFocus`.
- La vista compacta contiene únicamente la tarjeta de estado; tiempo/fase/perfil, advertencias y rutas disponibles se distinguen claramente, los controles no se pegan y los detalles plegables respetan 340/220 DIP.
- React tests, solución Release, scripts del instalador y manifiesto/hash de paquete pasan.
- Se documenta que el paquete temporal no sustituye la instalación ni la aceptación de servicio/IPC.

## Resultado

Implementado y validado el 2026-10-06. React: 95/95; `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings/0 errors; installer script tests pasan; Setup `--package-self-test` exit0; manifest31/31, ZIP1732; Graphify10155 nodos/27380 aristas/986 comunidades. Setup SHA-256 `D43626C4DC3111903B30891A3343A16D648E5FC132D65E4AD0B7E387C973C9D2`; ZIP SHA-256 `8C13E50300F06D015165DB58960E478E5A52EB6AE778B314957B04934421B873`.

El Host 0.1.76 temporal mostró la vista full a 1216×799 y el compacto a 480×340 DIP (ventana exterior 496×379 px). El Host 0.1.71 instalado se abrió sin `--pilot`, se inspeccionó el tab Profile en lectura y se cerró; el servicio continuó `Running`. El Host 0.1.76 no se instaló; su instancia temporal no puede validar IPC instalado. El paquete 0.1.75 y el baseline 0.1.71 permanecen preservados.
