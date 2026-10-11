# Focus Cockpit Page Overrides

> **PROJECT:** FocusGuard
> **Generated:** 2026-10-10 11:43:40, por ui-ux-pro-max (`--design-system --persist --page focus-cockpit --variance 6 --motion 5 --density 3`). Editado a mano con las decisiones de FG-414.
> **Page Type:** Cabina de foco (escritorio WebView2)

> ⚠️ **IMPORTANT:** Rules in this file **override** the Master file (`MASTER.md`).
> Only deviations from the Master are documented here. For all other rules, refer to the Master.

---

## Adoptado del Master

- Patrón «Minimal Single Column»: una acción principal grande y centrada («Revisar sesión»), mucho espacio vacío y la navegación fuera del camino (la cabina inmersiva la oculta).
- Lista previa a la entrega: sin emojis como iconos (Radix Icons), `cursor:pointer` en lo clicable, hover de 150–300 ms, contraste de texto ≥ 4.5:1 en claro y oscuro (lo comprueba `verify-dist`), foco visible, `prefers-reduced-motion` respetado y diseño adaptable.
- Áreas táctiles de 44 px en controles compactos (pseudo-elemento de alcance, sin agrandar lo visual).

## Overrides (y por qué)

### Color

- **No** se usa la paleta propuesta (rojo #DC2626 / verde #059669 sobre #0F172A). El brief pide un estilo zen, atmosférico e inspirado en Opal, y el rojo de foco transmite urgencia.
- Se usan cuatro ambientes pastel (Aurora lila, Bruma verde-agua, Ámbar, Océano) en claro y oscuro, con un único acento por ambiente. Los tokens semánticos de texto los verifica `verify-dist` (oscuro 6.54:1, claro 5.61:1).

### Typography

- **No** se usa Inter. La CSP de la WebView (`font-src 'self'`) y el contrato de distribución solo admiten Geist empaquetado (`Geist-Regular`/`Geist-SemiBold`).
- La skill taste también desaconseja Inter por defecto. Escala: número del orbe 56–92 px, títulos 15–24 px y cuerpo 13–14 px con cifras tabulares.

### Effects

- **No** se aplica «No motion blur / sharp transitions (no fade)». El brief pide respiración y una cabina atmosférica, así que el movimiento sigue a Emil Kowalski (ease-out < 300 ms, solo transform/opacity, salidas más rápidas).
- Los fundidos con desenfoque se usan solo en cambios de estado (número del dial, entrada de la cabina). Todo se desactiva con movimiento reducido.

### Materials (Apple HIG, Materials)

- Vidrio (translucidez y brillo) solo en controles y navegación: barra lateral, píldoras de cabecera, segmentados y barra de guardado.
- El contenido (grupos de Preferencias, editores de reglas, cabecera de perfil, bandeja de inicio, tarjetas de estado) usa un material estándar más grueso (`--material`, 84–86 % opaco), sin brillo de vidrio.
- Con Reduce Transparency todo pasa a opaco (contrato en `verify-dist`).

## Componentes propios de esta página

- `FocusOrb` (dial `role=slider` 1–720 min, pasos de 5 en el anillo) y `FocusOrbScene` (three.js, chunk diferido, alternativa CSS sin WebGL).
- Chips de duración 5/15/25/50/90. El 5 va primero como micro-compromiso contra la procrastinación.
- Cabina inmersiva: atmósfera en movimiento, orbe-reloj, una frase y dos botones. En modo estricto la insignia y la ruta de recuperación siguen visibles.
