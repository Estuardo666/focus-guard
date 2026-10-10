# FG-241 — Contraste de campos editables en React

## Hallazgo

En los temas instalados .79, el token `--line` se usaba también para los bordes de campos. El borde oscuro `#343d40` alcanzaba 1.52:1 contra la superficie `#191d20`; el borde claro `#c6d2ca` alcanzaba 1.56:1 contra blanco. Cuando ese trazo es la señal que permite reconocer un campo, queda por debajo del mínimo 3:1 de WCAG 2.2, SC 1.4.11 ([W3C](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html)).

## Cambio

- Se agregó el token semántico `--control-line`, separado del borde sutil de las tarjetas: `#707a7c` en oscuro y `#6f7c75` en claro.
- Inputs de texto, selectores y áreas de texto usan el token. Checkbox y radio conservan su tratamiento actual.
- Forced-colors restaura `CanvasText`.
- El verificador del CSS de producción calcula contraste contra `bg`, `surface` y `raised` para ambos temas, exige ≥3:1 y comprueba que estén presentes los selectores y la regla forced-colors.

## Verificación

- `npm test`: 97/97.
- `npm run build`: TypeScript, Vite y `verify-dist.mjs` pasan. El verificador informa mínimo de borde 3.46:1 oscuro y 3.67:1 claro; los textos semánticos y label CTA tienen mínimo 6.20:1 oscuro y 4.94:1 claro.
- `dotnet test FocusGuard.slnx -c Release --no-restore`: 851/851.
- `extension npm test`: 19/19.
- `FocusGuard.UI.exe --smoke` desde el publish .80 carga el contenido local WebView2 y termina con exit 0. Ese modo no accede al servicio autenticado.
- Setup .80 empaquetado; su CSS incluido coincide por SHA-256 con el CSS React compilado. `FocusGuard.Setup.exe --package-self-test`: exit 0.
- `FocusGuard.Setup.exe --smoke-test`: exit 0; verificó el layout de Setup y el estado de finalización simulado, sin instalar.
- `test-browser-extension-package.ps1 -Version 0.1.80` y `test-installer.ps1`: pasan.
- `graphify update .`: 10,191 nodos, 27,523 aristas, 984 comunidades.
- Recibo: `D:\TEMP\focusguard-fg166-validation\artifacts\fg241\build-receipt.json`.

La primera generación Setup produjo un payload ZIP truncado de 97 KB y el autochequeo falló al leer su directorio central. La recompresión del mismo contenido dio un ZIP válido de 326,343,910 bytes y 1,731 entradas; una reconstrucción limpia produjo Setup/ZIP .80 válidos y el self-test pasó. La causa de esa primera truncación no quedó identificada; la salida fallida permanece separada en `D:\TEMP\focusguard-fg166-validation\artifacts\fg241\installer` y no se usa como entrega.

## Pendiente

Setup .80 todavía no está instalado: la máquina sigue ejecutando Host .79 y la consulta de token de Windows informa `Elevated=false`. No ejecuté el instalador ni cambié estado de máquina. Falta verificar visualmente el campo en tema oscuro, claro y forced-colors en el Host instalado. Esto se sigue en FG-242.
