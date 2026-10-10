# FG-327 — Contrato forced-colors para salud activa

Fecha: 2026-10-08
Estado: verificador de producción implementado y pasando; aceptación instalada de forced-colors/Narrator pendiente.

## Problema

FG-320 añadió las tarjetas Servicio/Extensión/Protección al overview de una sesión activa y ya define borde `CanvasText` en forced-colors. El verificador CSS de producción no protegía todavía ese límite; podía desaparecer sin fallar el build.

## Plan

1. Añadir en `scripts/verify-dist.mjs` una comprobación del selector compilado y del borde `CanvasText` dentro de `@media(forced-colors:active)`.
2. Ejecutar build React/Vite para validar el CSS minificado, CSP, contraste y viewport.
3. No cambiar estilos/producto ni regenerar Setup: sólo se endurece la validación del CSS existente.

## Aceptación

El build pasa con la regla actual y falla si falta el borde visible de las tarjetas activas bajo forced-colors. La aceptación visual instalada y Narrator siguen siendo gates separados.

## Verificación

`npm run build` pasó: TypeScript/Vite, contraste dark/light (controles ≥3:1; texto/CTA ≥4.5:1), layout mínimo, CSP/assets locales y el nuevo selector forced-colors minificado. No cambió la UI CSS ni el Setup .111; la regla `CanvasText` que ya está en .111 queda protegida ante regresiones. La aceptación real en WebView2/Narrator sigue pendiente.
