# FG-338 - Swatches de perfil compactos y accesibles

**Fecha:** 2026-10-08
**Fases:** 6 y 9
**Estado:** corrección de fuente y paquete .116 completos; inspección visual instalada pendiente.

## Auditoría y dirección de diseño

El control de color ya tenía un target de 44×44px, pero el estilo aplicaba el color de perfil a todo el botón, convirtiendo la paleta en bloques grandes. Se conserva el área de interacción y se reduce solo la marca visible a un círculo de 24×24px.

Se mantienen Geist, Catppuccin, los acentos existentes y la estructura del editor. La selección lleva un `CheckIcon` Radix y `aria-pressed`, por lo que el estado no depende solo del color. En colores forzados, el target usa `ButtonFace`/`ButtonText`; el borde de selección y el foco usan `Highlight`, mientras el swatch de perfil sigue siendo decorativo. No se añadió animación a un control que se cambia con frecuencia.

El ajuste sigue una evolución localizada: UI/UX Pro Max para targets/contraste, Taste para conservar la marca y la arquitectura, Frontend Design para eliminar el bloque excesivo, Emil y Apple para respuesta inmediata y feedback de estado sin motion decorativo.

## Cambio

- Botones de color con hit target de 44×44px y swatch circular de 24×24px.
- El color seleccionado conserva un check vectorial de la familia Radix y el nombre/estado accesible del botón.
- Forced-colors recupera la paleta del sistema para el target, el foco y el borde de selección.
- Se añadió una prueba React para la selección accesible y un contrato de CSS de producción para target, swatch y forced-colors.

## Validación

- `npm test`: 121 pruebas pasaron en 21 archivos.
- `npm run build`: TypeScript, Vite y `verify-dist.mjs` pasaron. Bordes editables: 3.46:1 oscuro y 3.67:1 claro; texto habilitado mínimo 4.94:1.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.116` y `scripts/test-installer.ps1`: pasaron.
- Cinco smokes del Host .116 terminaron con código 0.
- Setup .116 generado; manifiesto de 31 archivos, payload de 1731 entradas, 18/18 assets UI con hashes coincidentes y tres fuentes locales.
- Setup SHA-256: `766EFD112702D7A8A6807478413CD289EFA0179DB9D63EA2A7F8C78716AE273E`.
- ZIP de distribución SHA-256: `B4F2F308A7BD3BC87BFEF36B5A039F522117611BE0C8544D6FDC41CFC5BB4E82`.
- `graphify update .`: 1484 archivos extraídos; grafo de 11156 nodos y 39003 aristas. `graph.html` se omitió por el límite de 5000 nodos.

## Estado instalado

No se instaló .116. El Host .112 sigue PID23972 en `--pilot`; Service/Extension Ready, sesión Inactive, sin SessionId y Strict apagado. No se obtuvo una captura posterior ni una comprobación instalada de teclado/forced-colors. Las fases 4-10 siguen abiertas.
