# Fase 3: nombrar el modo claro/oscuro en Preferencias

## Resultado

La pantalla Preferencias ya no llama «Tema» al control que solo cambia claro/oscuro. Su etiqueta es «Modo claro/oscuro / Light/dark mode». La nota sobre compatibilidad con FocusGuard clásico especifica que paleta y estilo del orbe se quedan locales a esta app.

## Cambios

- Se actualizaron `themeLabel` y `preferencesSharedWithClassic` en EN/ES.
- `PreferencesPanel.test.tsx` verifica el rótulo renderizado y que la nota identifique paleta/orbe como locales.
- No se modificó el contrato de preferencias, el payload global ni el comportamiento de guardar.

## Verificación

- UI: **254/254 PASS**.
- WebView2 con UI empaquetada: **3/3 PASS**; el renderer real navega a Preferencias y confirma el rótulo «Modo claro/oscuro» y el aviso de paleta/orbe locales.
- El smoke WebView2 corre sobre las tres variantes existentes (UI temporal, packaged normal y packaged idle); el renderer packaged confirma el texto de `.183` sin servicio real.
- `npm run build`: TypeScript, Vite y `verify-dist` PASS; CSP, contraste, forced-colors y contratos de viewport PASS.
- Build del Setup autónomo y ZIP `.183`: PASS.
- Guard de producción: PASS; se verificaron los Client embebidos.
- `scripts/test-installer.ps1`: PASS, incluidos 8 casos de readiness.
- `--package-self-test`: exit 0.
- Graphify: PASS; 1.960 archivos AST, 24.857 nodos, 182.650 aristas y 2.002 comunidades.
- SHA-256 Setup: `D4F497C16AE1CE7C0D30A05D527264B9312DEF0C902DBCB54E97FF7949B18F6D`.
- SHA-256 ZIP menú: `AF74587E8E26BA03B730E990B38860F2D1F00A3279BB2A0CD7264C4636D6B7B0`.
- Host263/263 y Sync162/162 son resultados previos sin cambios de esas capas.

## Límites

`.183` fue generado y validado, pero no se instaló ni desplegó. La aceptación física de convergencia, reset offline/tombstone, overrides, transferencia portátil y conservación de preferencias locales todavía requiere la PC hermana. Staging permanece detrás de esa aceptación.
