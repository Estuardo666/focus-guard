# FG-342 — Mantener contexto al recorrer reglas de perfil

**Fase:** 6 y 9  
**Estado:** implementado en fuente y empaquetado en Setup .117; aceptación instalada pendiente.

## Evidencia y objetivo

En el Host .112 a 1216×799, `Perfiles` muestra el selector de perfiles en una columna izquierda y las reglas en la derecha. Al recorrer hacia abajo las diez reglas de sitios, la captura directa dejó fuera de la ventana la navegación global y la lista de perfiles. Eso oculta el contexto del perfil seleccionado durante una edición larga. `UX_SPEC.md` línea 109 pide conservar navegación y lista de perfiles junto al área de trabajo; el criterio de navegación de perfil también pide que la región tenga desplazamiento limitado por la ventana.

## Plan

1. Mantener pegada la navegación global durante el desplazamiento vertical sin cambiar su posición ni tamaño normal.
2. En el layout de dos columnas (más de 800 CSS px), mantener pegada la lista de perfiles y limitar su altura al viewport con desplazamiento interno cuando haga falta.
3. En ventanas estrechas, conservar el flujo apilado existente; la navegación compacta sigue disponible en la parte superior.
4. Alinear la versión del paquete UI y la extensión a .117; añadir un contrato de build para los breakpoints y probar TypeScript/React/Vite/CSP/layout antes de empaquetar.
5. Revisar visualmente los tamaños normal y compacto; actualizar `TASKS.md` y el checkpoint con la evidencia.

## Riesgo, recuperación y validación

Es una modificación CSS de presentación: no envía IPC, no guarda preferencias ni modifica reglas. El riesgo principal es que un rail pegado cubra el enlace de salto/foco o reduzca demasiado el espacio vertical. Por eso el selector sticky se limita al layout de escritorio, usa una altura máxima y mantiene el skip link en una capa superior; en el layout apilado no se fija la lista de perfiles. La reversión consiste en retirar las reglas CSS y su contrato.

El CSS ahora mantiene el rail global pegado en ventanas de escritorio y en el header compacto; en el layout de dos columnas mantiene también el selector de perfiles con desplazamiento interno cuando supera el viewport. Por debajo de 801px el selector sigue apilado y no se fija.

## Validación

- `npm test`: 121 pruebas pasaron en 21 archivos.
- `npm run build`: TypeScript, Vite, CSP, contraste, layout mínimo, reduced motion y el contrato de sticky passed.
- `scripts/build-installer.ps1 -Version 0.1.117`: generó Setup standalone de 442,853,560 bytes y ZIP de distribución de 699,170,750 bytes. Payload ZIP: 326,384,973 bytes.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.117`: paquetes Chrome/Edge pasaron.
- `scripts/test-installer.ps1`: pasó.
- Cinco smokes del Host .117 (`--smoke`, runtime missing, renderer failure, renderer crash y window DPI) terminaron con código 0.
- `graphify update .`: 1524/1524 archivos AST; grafo de 11218 nodos, 40104 aristas y 1026 comunidades.
- Evidencia visual .112 anterior al cambio: `artifacts/fg342-output-sticky/fg342-profile-top-0.1.112.png` y `artifacts/fg342-output-sticky/fg342-profile-site-scroll-0.1.112.png`. La segunda muestra que al recorrer la lista de sitios, navegación y selector de perfil salen del viewport.

El Host .112 PID29404 y el servicio LocalService PID13424 permanecen instalados; Service/Extension siguen listos, sin sesión activa y Strict apagado. El Setup .117 se construyó y probó, pero **no se instaló**. No hay captura posterior de .117 instalado; el sticky está verificado por el contrato de build, no por aceptación visual instalada.

El primer intento de empaquetado .117 falló antes de crear el manifiesto porque la versión de la extensión seguía en .116. Su salida parcial quedó en `artifacts/installer/FocusGuard-0.1.117-fg342-sticky` (771 MB); la salida válida está en `artifacts/installer/FocusGuard-0.1.117-fg342-sticky-retry1`. La revisión automática rechazó borrar recursivamente la carpeta parcial, por lo que permanece como limpieza pendiente.

## Siguiente paso

Instalar .117 desde una consola que ya esté elevada y revisar visualmente Perfiles a 1216×799, 1024×700, 800×600 y 720×480, verificando que el header no tape el skip link y que el selector sticky pueda desplazarse por separado. Mantener abiertas las pruebas de DPI/Windows 11, forced-colors, Narrator y las fases 4/7–10.
