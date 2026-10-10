# FG-244 — Selector accesible de presets de ciclo

## Hallazgo y cambio

El administrador de presets exponía un `div role="listbox"` con botones `role="option"`, pero no tenía gestión de flechas ni de foco de un listbox compuesto. Como la opción es única, se reemplazó por un `fieldset`/`legend` con radios nativos agrupados por `name`. El navegador maneja el movimiento de flechas y el marcado del radio activo; cada fila conserva la tarjeta de 48 px, el nombre, la etiqueta Built-in/Custom, el foco visible y el estado de selección. Forced-colors conserva límites del sistema.

La decisión sigue el [Radio Group Pattern de WAI-ARIA APG](https://www.w3.org/WAI/ARIA/apg/patterns/radio/), que especifica la selección única y el movimiento de flechas para grupos de radios. La prueba DOM confirma nombre del grupo, grupo común, estado inicial, cambio de selección y ausencia de roles listbox/option custom.

## Verificación y paquete

- `npm test`: 98/98; TypeScript y `npm run build` pasan, incluido el verificador de contraste.
- `dotnet test FocusGuard.slnx -c Release --no-restore`: 851/851.
- `extension npm test`: 19/19; package test .81 y `test-installer.ps1` pasan.
- Setup .81 `--package-self-test` y `--smoke-test`: exit 0.
- Host .81 `--smoke`, `--smoke-renderer-failure` y `--smoke-renderer-crash`: exit 0.
- Graphify: 10,197 nodos, 27,556 aristas, 984 comunidades.
- Recibo/Setup/ZIP .81: `D:\TEMP\focusguard-fg166-validation\artifacts\fg244\build-receipt.json`.

## Pendiente

La máquina sigue en Host .79 y el token de Codex no está elevado. Falta instalar .81 y probar visualmente el selector y su navegación con teclado dentro del WebView2 autenticado; esa prueba queda en FG-242.
