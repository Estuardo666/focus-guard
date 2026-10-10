# FG-254 — Selector compacto alcanzable en el Host Windows

## Hallazgo
La ventana completa del Host declara MinimumSize 720×480 DIP. FG-249 activaba el selector compacto únicamente bajo700 CSS px, un estado que no podía alcanzarse en el Host completo instalado. Eso dejaba siete tabs en una fila en el ancho mínimo real.

## Cambio
El breakpoint del shell/selector sube a720 CSS px, alineado con el mínimo de ventana. El Host mantiene la barra vertical en ventanas mayores y muestra el selector nativo localizado en su ancho mínimo. Labels, orden y estados disabled no cambian. El production CSS verifier busca el breakpoint después de la minificación Vite. No se agregó movimiento.

## Verificación
- React104/104; TypeScript y build verificados.
- CSS empaquetado contiene width<=720px, la barra de tabs oculta y el selector nativo visible. El test DOM confirma7 destinos, label accesible, disponibilidad y navegación a Ayuda.
- Contraste/CSP pasan; campos3.46:1 oscuro/3.67:1 claro, texto/CTA6.20:1/4.94:1 y forced-colors CanvasText.
- Extensión19/19, test estático instalador y tres Host .87 smokes pasan.
- Setup/ZIP .87 manifest correcto y1,732 entradas; 18 assets UI coinciden por SHA-256 con dist. Receipt: D:\TEMP\focusguard-fg166-validation\artifacts\fg254\build-receipt.json.

## Límite
La instalación actual .86 sigue abierta con una sesión normal activa; no cambié su ventana ni la sesión en esta fase. El paquete .87 no está instalado y no hay una captura visual a ancho mínimo. La revisión en Host instalado queda para FG-242.