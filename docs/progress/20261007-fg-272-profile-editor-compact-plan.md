# FG-272 — Hacer visibles las reglas del perfil antes del primer scroll

## Evidencia

La captura WebView2 instalada .91 a1216×799 muestra la identidad y administración del perfil desde y=283 hasta y=637; el panel principal de aplicaciones comienza en y=657 y queda casi entero fuera del primer viewport. En una herramienta de foco, las reglas que la persona vino a editar tienen menos visibilidad que nombre, color, duplicar y eliminar.

## Dirección de diseño

1. Mantener nombre y monograma del perfil en una barra compacta.
2. Mover renombrar, color, duplicar y eliminar a un disclosure nativo de “Ajustes del perfil”, cerrado inicialmente.
3. Dejar Aplicaciones y Sitios inmediatamente después de esa barra, con conteo y acciones guardadas claramente nombradas.
4. Conservar confirmación antes de eliminar, teclado, foco visible, ES/EN y el mismo layout bajo forced-colors.
5. Validar el primer viewport instalado a1216×799 y los breakpoints compactos antes de preparar el paquete siguiente.

## Aceptación

La captura inicial muestra el resumen del perfil y los encabezados/acciones de reglas sin abrir ajustes; abrir el disclosure expone nombre/color/acciones. Tests comprueban disclosure cerrado, navegación accesible y que eliminar continúa requiriendo review. Ninguna regla o comportamiento de enforcement cambia.

## Implementación en fuente

`ProfileEditor` ahora mantiene el perfil seleccionado en un resumen compacto; nombre/color/duplicar/eliminar están en un `<details>` nativo cerrado al cargar. Reglas guardadas aparecen antes del catálogo y de formularios de alta; ambos tipos tienen estados vacíos localizados. Confirmación de eliminación y mutaciones no cambian.

React109/109; TypeScript, build, CSP y contraste pasan. Setup/ZIP .93 incluye Host/Agent .92 y la nueva UI: manifest31/31, ZIP1732 entradas, paquetes Chrome/Edge14 entradas con mismo SHA; installer static, browser package y Host3 smokes pasan. Recibo `D:\TEMP\focusguard-fg166-validation\artifacts\fg273\build-receipt.json`.

La captura instalada/teclado real sigue pendiente: Setup .92 permanece abierto sin instalar y la máquina conserva .91. No afirmar aceptación visual de FG272 hasta instalar .93 y capturar a1216×799.
