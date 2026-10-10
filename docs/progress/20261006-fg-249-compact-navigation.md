# FG-249 — Navegación compacta sin desbordamiento

## Diseño y cambio
La barra tenía siete pestañas con icono y texto en una sola fila bajo700px. Como sus etiquetas mantienen ancho mínimo, podían desbordar una ventana estrecha. Se conserva la barra de pestañas vertical en escritorio y, bajo ese breakpoint, aparece un select nativo etiquetado para cambiar entre los mismos siete destinos. El select conserva el orden, la traducción y las reglas de disponibilidad, y actualiza la pestaña activa. El tablist de escritorio ahora tiene el nombre accesible Navegación principal.

La solución usa el control nativo del WebView para el viewport estrecho, conserva targets de44px y no añade movimiento. La identidad visual, orden de destinos y lógica de los tabs no cambian.

| Antes | Después | Por qué |
| --- | --- | --- |
| Siete iconos y etiquetas se comprimían en una fila bajo700px | Select localizado de ancho completo, con el destino activo y disponibilidad de cada opción | Evita overflow y conserva una forma familiar de navegación por teclado |

## Verificación
- React:104/104; la prueba nueva cambia a Ayuda con el select y comprueba siete destinos, nombre del selector y opciones deshabilitadas.
- TypeScript/build de producción y CSS verifier pasan; el bundle conserva el select etiquetado y oculta la fila horizontal bajo700px.
- El layout de Perfiles apila Aplicaciones y Sitios hasta1450px. Contraste continúa campos3.46:1 oscuro/3.67:1 claro, texto/CTA6.20:1/4.94:1 y forced-colors CanvasText.
- Extensión19/19; test estático del instalador pasa.
- Host .85 empaquetado: tres smokes locales exit0.
- ZIP .85: manifest correcto,1,732 entradas, 18 archivos UI coinciden con el dist por SHA-256. Paquetes de extensión Chrome/Edge validados sin archivos de desarrollo/secretos.
- Graphify:10,223 nodos,27,749 aristas,1,001 comunidades. Hashes: D:\TEMP\focusguard-fg166-validation\artifacts\fg249\build-receipt.json.

## Límites y siguiente paso
El paquete .85 no está instalado; Setup self-test/UI smoke no se ejecutaron. Host instalado sigue en .79 y PID32316 quedó intacto. La prueba DOM/CSS y el package smoke no sustituyen una inspección visual a320px/700px en WebView2. FG-242 debe instalar .85 con el propietario y probar selector, foco/teclado, forced-colors y temas. Fase6 y las fases posteriores siguen abiertas.