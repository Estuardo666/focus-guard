# FG-226 — Plegado de detalles compactos

## Resultado

La vista compacta React ahora conserva el esquema de Classic: fase, tiempo, perfil y avisos persisten al plegar; fin exacto, objetivo, salud del servicio, TopMost y explicación de cierre quedan en detalles expandibles. `Más detalles/Ocultar detalles` usa `aria-expanded` y `aria-controls`. El Form nativo ajusta altura de 480×340 a 480×220 DIP y vuelve al tamaño/bounds completos que tenía antes.

El estado `DetailsExpanded` forma parte del bridge tipado `window.compactDetails.set` y del mensaje `host.windowState`, ligado al documento vigente. El Form cambia sólo su altura; la sesión y el servicio no participan. La vista plegada mantiene refresco, avisos de bloqueo no confirmado y la ruta a la vista completa disponibles.

## Verificación

- `npm run check`: correcto.
- `npm test`: 20 archivos, 93/93 pruebas aprobadas. Cubre que fase/tiempo/perfil/aviso siguen visibles al plegar y que los detalles se representan al expandir.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: 149/149 aprobadas, incluida la transición Form 340↔220 DIP y restauración de vista completa.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings / 0 errors; verificador CSP y assets locales pasan.
- `scripts/test-installer.ps1`: pasó.
- `graphify update .`: 10.112 nodos, 27.253 aristas, 975 comunidades; `graph.html` se omite por el límite visual del grafo.
- Setup standalone 0.1.74: manifiesto 31/31, ZIP menú 1.732 entradas. Setup externo/incluido SHA-256 `D7410C93F0717D77562C33C8D09053D44A6DB3209681D130657C446D8D3C6B1B`; ZIP SHA-256 `1B33908960BF580344D013BAC4A528237D1DA49DD95AF195EE739E819039659A`. `CompactFocus.js` en E `dist` y payload coincide: `DCA91890C74ADE69A0094D4AA58A62EE8E49FC147205B78169585DA9483F0E26`.
- El Setup/ZIP 0.1.73 de FG-225 se conservó intacto; esta equivalencia se publica en 0.1.74.

## UX, seguridad y límites

Plegar es instantáneo y mantiene el foco en controles visibles. La barra nativa ofrece arrastre; el bridge verifica origen/token/parámetro y no toca sesión ni Storage. Classic continúa como único dueño del tray.

0.1.74 no se instaló: esta ejecución no posee token elevado. No se obtuvo captura visual de WebView2 porque `orca` no está disponible. Captura instalada, escalado multi-monitor y drag real siguen pendientes.

## Siguiente paso

Completar F223 con una vía aprobada para probar el bloqueo web, después instalar y validar visualmente 0.1.74. Bandeja/toasts siguen pendientes con handoff único desde Classic.
