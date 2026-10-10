# FG-329 — Regresión de movimiento reducido

Fecha: 2026-10-08
Estado: regresión App y gate de CSS de producción implementados; aceptación instalada pendiente.

## Problema

App aplica `UserPreferences.reduceMotion` a `documentElement.dataset`, y el CSS desactiva movimiento por preferencia guardada o por `prefers-reduced-motion`. No había una regresión de App que probara la propagación de la preferencia, ni un gate del build CSS que protegiera ambos caminos.

## Plan

1. Añadir una prueba App que cargue una preferencia persistida de movimiento reducido y confirme idioma/dataset aplicado.
2. Hacer que `verify-dist` exija tanto el override de preferencia guardada como el media query de sistema en CSS de producción.
3. Mantener el Host/política sin cambios; se endurece cobertura del contrato existente.

## Aceptación

La preferencia almacenada pone `data-reduce-motion=true`; CSS de distribución elimina transiciones, animaciones y scroll suave bajo ese override y bajo el media query del sistema. La aceptación en Narrator/forced-colors instalado permanece separada.

## Verificación

- `src/FocusGuard.UI/src/App.test.tsx` carga preferencias ES/EN con `reduceMotion=true` y confirma `document.documentElement.dataset.reduceMotion === 'true'`.
- `scripts/verify-dist.mjs` ahora comprueba en CSS compilado el override almacenado y `prefers-reduced-motion: reduce`, incluidos transition, animation y scroll-behavior.
- `npm test`: React119/119; `npm run build`: TypeScript/Vite, contraste, viewport, CSP, assets locales, forced-colors y ambos contratos reduced-motion pasan.
- No se cambió UI productiva ni el Setup .112: el parche es de pruebas/build gates. La aceptación visual/Narrator instalada sigue pendiente.
