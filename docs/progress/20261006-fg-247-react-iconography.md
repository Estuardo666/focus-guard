# FG-247 — Iconografía coherente en React

## Diseño y cambio
El audit del dashboard existente halló siete glifos Unicode distintos en la navegación y otros glifos para el selector de tema. La captura .79 muestra los tamaños y pesos de esos símbolos desiguales. Se conserva el contrato visual graphite/mint, Geist, la barra lateral y la navegación actual; no se añade movimiento a una acción de uso frecuente. Se adopta una sola familia Radix Icons, renderizada a18px. Las etiquetas visibles siguen identificando cada destino; los SVG decorativos se excluyen del árbol accesible. El nombre accesible del botón de tema se conserva.

| Before | After | Why |
| --- | --- | --- |
| Glifos Unicode de formas/tamaños distintos para Resumen, Perfiles, Actividad, Programación, Preferencias, Conexión y Ayuda | Iconos Radix vectoriales a18px en una única familia; las mismas etiquetas y orden quedan visibles | Alinea peso, escala y lenguaje visual sin cambiar la orientación ni las funciones |
| Sol y luna como caracteres tipográficos | SunIcon/MoonIcon decorativos dentro del mismo botón con su aria-label existente | Mantiene un control predecible y su nombre comprensible para tecnología de asistencia |

## Verificación
- Prueba DOM nueva confirma las siete etiquetas en orden, un SVG decorativo por destino y el nombre accesible del control de tema.
- React:103/103; TypeScript y build de producción pasan. Verificador: campos editables3.46:1 oscuro/3.67:1 claro; texto/CTA6.20:1/4.94:1; forced-colors CanvasText.
- Extensión:19/19. scripts/test-installer.ps1 pasa.
- Host empaquetado .83: --smoke, --smoke-renderer-failure y --smoke-renderer-crash, todos exit0.
- ZIP .83 abre, manifest versión .83 y1,732 entradas; 18 assets UI empaquetados coinciden por SHA-256 con el dist recién compilado. Chrome/Edge requeridos están presentes; no contiene entradas de desarrollo/secretos.
- Graphify:10,213 nodos,27,609 aristas,994 comunidades. Hashes de Setup/ZIP/extensión en D:\TEMP\focusguard-fg166-validation\artifacts\fg247\build-receipt.json.

## Límites y siguiente paso
El .83 no se instaló. Setup package self-test y su smoke gráfico no se ejecutaron; el token de Codex no está elevado. La única ventana activa observada antes del cambio es la UI del equipo, PID32316; se dejó intacta y corresponde al Host instalado anterior. Por eso este checkpoint verifica el DOM, el CSS empaquetado y smokes del Host, pero no afirma una revisión visual instalada de los iconos. FG-242 debe validar .83 en WebView2, teclado, dark/light y forced-colors. Fase6, paridad y sustitución siguen abiertas.