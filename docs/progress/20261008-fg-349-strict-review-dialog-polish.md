# FG-349 — Pulido de la revisión estricta (foco, razones, caducidad, centrado)

**Fecha:** 2026-10-08 local / 2026-10-09 UTC  
**Fase:** 8, strict/recuperación; aceptación instalada en Fase 9  
**Estado:** cambio de presentación probado en fuente, empaquetado en Setup .119 e **instalado y aceptado en el Host real** (ver FG-350; la corrección del literal `{minutes}` llegó en .120).

## Alcance

Auditoría del diálogo de revisión estricta de React (`StrictReviewPanel`) contra `UX_SPEC.md` (§Strict confirmation) con las skills de diseño pedidas: UI/UX Pro Max (accesibilidad, foco, estados deshabilitados), Emil (movimiento: sólo entrada, 180 ms, ease-out fuerte, nunca `scale(0)`), Taste y Frontend Design (jerarquía y restricción: sin decoración nueva, un solo acento) y la guía de Apple HIG sólo como criterio de claridad de confirmaciones destructivas (no hay una skill Apple dedicada en esta sesión). No se cambió IPC, protocolo, autorización, enforcement ni la lógica de activación.

## Hallazgos y cambios

| # | Hallazgo | Cambio |
| --- | --- | --- |
| 1 | **El diálogo aparecía pegado arriba a la izquierda** (medido: `margin:0`, `left:0`, `top:0` en un viewport de 985 px). El reset de Tailwind elimina el `margin:auto` del navegador; UX_SPEC pide un modal centrado. | `.strict-review-dialog{margin:auto}` y contrato en `verify-dist.mjs`. |
| 2 | Al pasar del paso 1 al 2 (y volver), el botón pulsado se desmonta y el foco cae al diálogo; un lector de pantalla no oye el nuevo título. | El `h2` (`tabIndex=-1`) recibe el foco cuando cambia el paso, no al abrir. |
| 3 | El botón de activar queda deshabilitado sin decir por qué. | Bloque `role="status"` con lo que falta («Marca que guardaste tu trabajo…», «Escribe el nombre exacto del perfil.») o «Todo listo…»; el botón lo referencia con `aria-describedby`. |
| 4 | La preparación caduca (130 s) y el diálogo se cerraba sin aviso, perdiendo lo escrito. | Cuenta visible «Esta revisión caduca en m:ss…» (componente aislado, no re-renderiza las listas) y un único aviso `role="status"` cuando quedan ≤30 s. |
| 5 | UX_SPEC pide fundido corto y fondo atenuado y desenfocado; sólo había atenuado. | Entrada de 180 ms (opacidad + 8 px + `scale(.98)`) y `backdrop-filter:blur(6px)`. Sin movimiento con `prefers-reduced-motion`, con la preferencia guardada (`data-reduce-motion`) ni con `prefers-reduced-transparency`; sin blur en forced-colors. `::backdrop` no lo cubre el reset global `*`, así que tiene reglas propias. |
| 6 | Con ~760 px de alto, botón y requisitos quedaban bajo el pliegue. | Pie `sticky` (requisitos + caducidad + acciones) en ventanas de ≥601 px de alto; en más bajas sigue el flujo normal. |
| 7 | Eyebrow de 10 px y grupo del código sin rol. | Eyebrow del diálogo a 12 px; `role="group"` en el código. |

Copia nueva (ES/EN, paridad probada): `strictPendingAcknowledge`, `strictPendingTyping`, `strictReadyToStart`, `strictReviewExpiresIn`, `strictReviewExpiringSoon`.

Se descartó revisar un bloqueo con Esc durante la activación: al confirmar, el cliente pone `strictReview: null` y el panel se desmonta, por lo que ese estado no se alcanza.

## Validación

- `npm test`: 124/124 en 21 archivos (dos pruebas nuevas: foco y requisitos; caducidad y aviso de 30 s con timers falsos).
- `npm run build`: TypeScript, Vite, CSP, contraste (4.5:1 texto, 3:1 bordes), layout mínimo, reduced motion y el nuevo contrato «Strict review dialog centering and motion» pasan.
- Comprobación visual en el navegador integrado con una página estática (CSS construido + marcado del paso 2; no es el Host): centrado, fondo desenfocado, viñetas de requisitos, pie fijo con botón deshabilitado visible. Estilos computados: animación `strict-review-in`, `blur(6px)`, eyebrow 12 px. La página y el servidor temporal estaban fuera del repositorio y se retiraron.
- Un primer `npm run build` falló con un error de rolldown que no se capturó; dos reejecuciones consecutivas pasaron sin cambios. Se trata como transitorio, no explicado.

## Empaquetado .119 (no instalado)

- `scripts/build-installer.ps1 -Version 0.1.119` con salida aislada: manifest `win-x64` con 31 archivos, 0 ausentes; los archivos de `src/FocusGuard.UI/dist` coinciden byte a byte con los del payload.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.119`: Chrome y Edge pasan. `scripts/test-installer.ps1`: pasa.
- Cinco smokes del Host del payload .119 (`--smoke`, `--smoke-runtime-missing`, `--smoke-renderer-failure`, `--smoke-renderer-crash`, `--smoke-window-dpi`): todos exit 0. Esta sesión **estaba elevada** y el Host se niega a arrancar elevado (exit 2 por diseño, `Program.cs`): la primera pasada dio 2 en los cinco y no cuenta. Se repitió con token de usuario básico (`runas /trustlevel:0x20000`, reduce privilegios, sin diálogos). Salidas en `artifacts/fg349-host-smokes-0.1.119/`.
- Setup: `artifacts/installer/FocusGuard-0.1.119-Setup.exe`, 442853560 bytes, SHA-256 `5F70D82ECBEFEEFB04FA8D24AAAAF2B9C5099738C1BB80600093D520542CFB3E`.
- ZIP de distribución: `artifacts/installer/FocusGuard-0.1.119-win-x64-menu.zip`, 699173512 bytes, SHA-256 `5750771AC65577A762D357503BD7C747B00CF2436A0CEF89C644E04719B366BA`.
- Payload: `artifacts/setup-payload/0.1.119-fg349-strict-dialog/FocusGuard.Payload.zip`, 326386028 bytes, SHA-256 `BC699E917912494638147585F0F54FF27791B8153D0E097E6038F037A989D4F2`.
- `graphify update .`: 11321 nodos, 41316 aristas, 1044 comunidades.
- Actualización posterior: con autorización explícita del propietario se instaló .119 y luego .120 (FG-350). El arreglo de Preferencias de .118 resultó insuficiente y se corrigió en .120.

## Límites

- **Sin salida animada:** el diálogo se desmonta al cerrar, así que el fundido de cierre de UX_SPEC sigue pendiente (requiere retrasar el desmontaje desde `DashboardStart`).
- **Frase de confirmación:** UX_SPEC describe una frase aleatoria de dos palabras en inglés; el producto usa el nombre exacto del perfil. No se cambió; es una divergencia previa de especificación a decidir por el propietario.
- No se probó en el Host instalado (.112 sigue instalado), ni con Narrator, forced-colors reales, DPI alto ni Windows 11.
- `.session-end-dialog` probablemente tiene el mismo problema de centrado; queda como tarea aparte.

## Seguridad y UX

Sin cambios de seguridad: el código de recuperación, la exigencia de guardarlo, el reconocimiento de trabajo sin guardar y el nombre exacto siguen siendo obligatorios antes de activar; la caducidad sigue gobernada por `expiresUtc` y `cancelStrictReview`. La cuenta atrás es sólo informativa. Reversión: retirar las reglas CSS añadidas al final de `tokens.css`, el `footer`/`ReviewExpiry` de `StrictReviewPanel.tsx` y el contrato de `verify-dist.mjs`.

## Siguiente paso

Instalar .119 desde una consola ya elevada (el proceso de Codex no está elevado y AGENTS.md prohíbe automatizar UAC) y revisar el diálogo real en el Host; después seguir con captura/reflexión, repetición, horarios reales y bandeja/avisos.
