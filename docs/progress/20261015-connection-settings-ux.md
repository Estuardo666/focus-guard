# 2026-10-15 — Conexión: UX de ajustes entre dispositivos

1. Summary: la sección Conexión agrupa sync, transferencia única y archivo portátil bajo "Ajustes entre dispositivos". Descripciones largas pasan a tooltips (InfoTip, `role=tooltip` + `aria-describedby`); sync muestra píldora de estado y chips Compartido/Solo esta PC; restablecer/desactivar viven en un popover nativo (`popover=auto`). Solicitudes de dispositivos con botón icono de actualizar.
2. Files: src/FocusGuard.UI/src/AccountPanel.tsx, AccountPanel.test.tsx, locales/es.json, locales/en.json, design-system/zen.css.
3. Tests: aserciones de tooltip, píldora de estado y menú popover en AccountPanel.test.tsx.
4. Commands: vitest run, npm run build, scripts/build-installer.ps1 -Version 0.1.178, scripts/test-installer.ps1.
5. Results: React 254/254; build y verify-dist PASS; instalador FocusGuard-0.1.178-Setup.exe SHA256 592dd5acff83684edd7c6f602574413f3e6ae76c634c0c707b0237ef6f43c16d; test-installer PASS.
6. UX: menos texto visible, acciones destructivas fuera del flujo principal pero con aviso de alcance en el menú; reduced-motion/transparency y forced-colors cubiertos.
7. Security: sin cambios de comportamiento; mismas llamadas al cliente.
8. Limitations: sin verificación visual en WebView2 instalado; el posicionamiento del menú usa CSS anchor positioning (Chromium/WebView2).
9. Next: aceptación visual instalada.
