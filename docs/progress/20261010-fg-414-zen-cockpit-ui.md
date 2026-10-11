# FG-414: rediseño zen de la UI React (cabina de foco)

## Resumen

Petición del propietario: una interfaz minimalista y atmosférica, inspirada en Opal para Mac, con poco texto y un bloqueo fácil de activar. Sin cambios de lógica: el bridge (`status.get`, `profiles.list`, `session.prepareNormal`/`startNormal`, `session.intent.get`, etc.) y los clientes `native/*` no cambian. Solo cambian la composición y la presentación.

- **Orbe de foco** (`FocusOrb.tsx` + `FocusOrbScene.tsx`): una perla WebGL (three.js, shader de ruido simplex) que respira a 6 respiraciones por minuto y se inclina hacia el cursor con un seguimiento amortiguado. Lleva un halo (framer-motion) que respira al mismo ritmo y un anillo SVG.
  - En reposo, el anillo es un **dial accesible** (`role=slider`, teclado ←↑→↓/PgUp/PgDn/Inicio/Fin, rango 1–720 alineado con el servicio; arrastre en pasos de 5 min sobre la banda del anillo). Hay chips rápidos de 15/25/50/90 min. El campo numérico del formulario sigue siendo la fuente de verdad que se envía a `session.prepareNormal`.
  - Durante la sesión, el anillo muestra la fracción restante calculada con `remainingSeconds` del servicio sobre `endUtc − startUtc`.
  - Sin WebGL (jsdom, contexto perdido) se usa un orbe CSS. three.js vive en un chunk diferido (`chunks/FocusOrbScene.js`) y solo se carga si hay WebGL.
- **Cabina inmersiva**: se abre al empezar cada sesión confirmada (`phase=ready`, `Active/Activating`, no compacta). Oculta la barra lateral y la cabecera, muestra el orbe con el reloj, «Respira. Solo esto ahora.», fin exacto, perfil, objetivo, controles y los tres canales como píldoras. «Ver panel» (o Esc, si no hay un diálogo abierto) vuelve al panel; «Volver al foco» la reabre. Una sesión nueva la reabre. Un estado no confirmado (stale) la cierra.
- **Ambientes**: Aurora (lila, la referencia), Bruma, Ámbar y Océano, en claro y oscuro. Solo recolorean el orbe, la atmósfera y el acento. Es una preferencia local del visor (`localStorage`, con try/catch); el tema claro/oscuro sigue guardándose en el servicio.
- **Atmósfera**: tres campos de color difuminados que derivan despacio (más rápidos y más visibles en la cabina). La barra lateral lleva un degradado del ambiente sobre el rail.
- **Menos texto**: se ocultan visualmente el subtítulo del resumen, el título y la explicación del bloque de inicio, y la nota duplicada de la barra lateral. Se mantienen visibles las consecuencias: revisión con fin exacto, aviso de trabajo sin guardar, detalle del modo estricto y ruta de recuperación.

## Archivos

- Nuevos: `src/FocusOrb.tsx`, `src/FocusOrbScene.tsx`, `src/design-system/zen.css`, `src/design-system/palettes.ts`, pruebas `src/FocusOrb.test.tsx`, `src/ZenCockpit.test.tsx`, `src/design-system/palettes.test.ts`; vista previa de desarrollo `dev/preview.html` + `dev/preview-host.ts` (host WebView2 simulado; no entra en `vite build`, que solo emite `index.html`).
- Modificados: `App.tsx` (orbe como reloj, cabina, ambientes, `useLayoutEffect` para los tokens del documento), `DashboardStart.tsx` (dial + chips), `main.tsx` (capa zen), locales es/en (8 claves), `scripts/verify-dist.mjs` (chunk `FocusOrbScene.js` permitido), `package.json` (`framer-motion@14.1.0`, `three@0.186.1`, `@types/three@0.186.0`; versión .140), `extension/manifest.json` (.140).

## Verificación

- React 161/161 (12 nuevas: dial/teclado/límites, fallback sin WebGL, cabina abierta/cerrada/reabierta, estricto visible en la cabina, ambiente persistido, **chip → `session.prepareNormal` con `durationSeconds` correcto**, dial deshabilitado sin modo piloto). `tsc` limpio.
- `npm run build`: contratos de `verify-dist` OK. Contraste: oscuro 6.54:1 / claro 5.61:1 (antes 6.20 / 4.94); bordes 3.78 / 3.64.
- Bundle sin `eval`, `new Function`, `<style>` inyectado ni `insertRule`. La build de producción servida con su CSP meta real (más el host simulado como script del mismo origen) renderiza el orbe WebGL, la cabina y las animaciones sin violaciones de CSP en la consola, y el reloj avanza con el sondeo real del cliente.
- Manual en el panel del navegador: dial arrastrado a 60 min; revisión con fin exacto devuelta por el host; cabina activa (normal y estricta, oscuro y claro); «Ver panel»/«Volver al foco»; 520 px sin desbordamiento horizontal (corregido).

## UX

Un solo gesto principal por pantalla: girar o elegir la duración y luego «Revisar sesión». La revisión con fin exacto sigue siendo obligatoria (UX_SPEC). En la cabina siguen visibles la insignia de modo estricto, el aviso y la ruta de recuperación, y Detener sigue deshabilitado en estricto. Movimiento reducido (sistema o preferencia): sin deriva atmosférica, sin respiración y sin seguimiento del cursor; el orbe queda quieto. Colores forzados: se ocultan el halo, la atmósfera y el lienzo; el anillo y el texto pasan a CanvasText.

## Seguridad

Sin nuevos permisos, red ni mensajes del bridge. La CSP no cambia (`script-src 'self'`, `style-src 'self'`, `worker-src 'none'`): framer-motion y three.js solo escriben estilos por CSSOM y WebGL no requiere relajarla. El ambiente se guarda solo en el almacenamiento local de la WebView y no contiene datos sensibles. El host simulado vive en `dev/` y no se empaqueta (`verify-dist` sigue rechazando `mockBridge`/`demoSession`).

## Limitaciones

- `host.js` crece a ~544 kB (165 kB gzip) por framer-motion. three.js (~529 kB) es diferido.
- Los ambientes no se sincronizan con el servicio (preferencia por visor).
- No se probó todavía en la WebView2 instalada ni con DPI 125/150 %, Narrator o Windows 11 (checklist con el propietario).

## Segunda ronda (pedido del propietario: orbe premium, movimiento, Perfiles y Preferencias)

- **Orbe premium**: el cuerpo es un mármol líquido (fbm con domain warping) dentro de una perla lechosa, con iridiscencia de película fina hacia el borde y dos brillos (clave y relleno). Además lleva una cubierta de vidrio aditiva (borde iridiscente y brillo duro), dos hilos de luz orbitando (como en la referencia) y dithering contra el banding.
- **Movimiento** (reglas de Emil Kowalski): entradas con ease-out por debajo de 300 ms, solo `transform`/`opacity`, nunca desde `scale(0)` y salidas más rápidas.
  - Diálogos: aparecen desde 0.96 con el fondo en fade y desenfoque.
  - Paneles y desplegables: suben 6–8 px. Avisos: bajan desde arriba. Chips y perfiles: entran con escala.
  - La cabina entra con un leve desenfoque. Los controles segmentados usan una «píldora» compartida (`layoutId`) que se desliza.
  - Dial: el asa sigue el puntero sin retraso al arrastrar, crece al agarrarla y se asienta con resorte ante chips o teclado; el número cambia con un fundido con desenfoque.
  - `MotionConfig reducedMotion` respeta la preferencia guardada.
- **Perfiles reestructurados**:
  - Una tira horizontal de perfiles (punto de color, conteos, crear al final) sustituye al riel lateral con formulario.
  - El perfil es una cabecera con monograma de acento y la línea «N sitios con regla de bloqueo · M aplicaciones…»; al abrirla se editan nombre, color, duplicar y eliminar.
  - Sitios primero, luego aplicaciones: alta rápida arriba (campo + segmentado Bloquear/Permitir + Añadir) y reglas como **chips agrupados** en «Bloqueados» y «Siempre permitidos». El tipo de coincidencia solo se muestra si no es el nombre del ejecutable. La regla manual avanzada y las excepciones de descanso siguen disponibles.
- **Preferencias reestructuradas**: tres grupos.
  - **Apariencia**: tema segmentado con iconos, ambientes, idioma y reducir movimiento.
  - **Sesiones**: duración con chips + número, tipo segmentado y bloqueo por defecto como interruptor.
  - **Al terminar una sesión**: cuatro tarjetas con un boceto de cada presentación, más los avisos de fase.
  - La barra «Cambios sin guardar · Descartar · Guardar preferencias» solo aparece si hay cambios.
- **Bug de empaquetado encontrado y corregido**: el Host lista cada chunk de la UI de forma explícita (csproj, `WebViewSecurityPolicy`, `build-installer.ps1` ×2 y `EmbeddedPackage.cs`). El primer .140 se generó **sin** `FocusOrbScene.js`, así que en la app instalada la importación diferida habría fallado. Ahora:
  - `FocusOrbScene.js` y el nuevo chunk compartido `jsx-runtime.js` están en todas las listas, con pruebas de política en `BridgePolicyTests`.
  - `verify-dist.mjs` falla si algún chunk emitido no aparece en esas fuentes.
  - Si el chunk del orbe no carga, se usa el orbe CSS en lugar de llegar al error boundary.
- Pruebas: React 167/167 (6 nuevas en esta ronda: agrupación de reglas, segmentado → `profile.website.add` con `action: 'Allow'` y revisión, cabecera de bloqueo, barra de guardado/descartar, guardado de tema + duración + fin de sesión + interruptor vía servicio, ambientes). Host.Windows 164/164.

## Tercera ronda (cierre de huecos)

- **Skills aplicadas**:
  - `ui-ux-pro-max`: design system y búsquedas `ux` + `--stack threejs`. Se descartaron su paleta roja y la fuente Inter porque contradicen el brief (Opal/zen) y la CSP solo admite Geist local.
  - Se aplicó lo relevante: áreas táctiles de 44 px para el borrar de los chips, los swatches de la cabecera y los segmentados (sin agrandar lo visual), devicePixelRatio ≤ 2 y `dispose()` (ya presentes).
  - `cloudai-x/threejs-skills` (`threejs-shaders`, `threejs-interaction`): el núcleo de la perla pasa a opaco («avoid transparent objects / limit overdraw»; solo la cubierta es aditiva). Con movimiento reducido se dibuja una vez por cambio en vez de cada fotograma. Puntero normalizado al rect del elemento, delta time y shaders sin condicionales (ya presentes).
  - «apple design»: no existe ninguna skill instalada o disponible con ese nombre; la búsqueda no devolvió resultados.
- **Prueba de ventana corregida, no omitida**: `CompactWindowResizesAndRestores…` comparaba con el tamaño anterior a crear el HWND (1200×760). El Host restaura a propósito con `KeepWindowOnScreen` (vuelve al tamaño previo dentro del área de trabajo). La prueba ahora crea el handle y espera ese tamaño en pantalla, que en una pantalla mayor que la ventana es exactamente el original. 3/3 en aislamiento; Host 164/164.
- .NET completo 921/921 (14 proyectos). React 167/167.
- **Backend real**: el servicio instalado (.139) está en marcha, pero autoriza IPC solo a binarios de confianza dentro de `Program Files\FocusGuard` (comprueba la ruta del proceso cliente). Probar la nueva UI contra el servicio real exige instalar la .140, lo que cambia la máquina y requiere la autorización del propietario (AGENTS.md).
  - Las pruebas de puente son unitarias: transporte simulado en tests y host simulado `dev/preview-host.ts` en la vista previa, no el servicio real.
  - Lo que sí se verificó en código real: los clientes `native/*` y los contratos no cambiaron, la política de recursos del Host incluye los chunks nuevos y el payload empaquetado es idéntico byte a byte a la build.

## Aceptación instalada contra el servicio real (autorizada por el propietario en el chat, 2026-10-10)

- Antes de instalar: DrillClient `status`, sin sesión activa (la última, Expired y normal).
- `install-focusguard.ps1 -PackageDirectory <ruta absoluta>\FocusGuard-0.1.140 -ClientMetadataQueryConsent` desde un contexto ya elevado: exit 0, health IPC autoritativo correcto, servicio Running/Auto como LocalService. `ui\host.js` y `ui\chunks\FocusOrbScene.js` instalados coinciden por hash con `dist/`. El Host `FocusGuard.UI.exe --pilot` se abrió con un acceso directo vía Explorer (integridad media).
- En la app instalada apareció un **anillo gris** alrededor del orbe con tema claro: la cubierta aditiva escribía alfa en el canvas transparente. Se corrigió con fusión «solo luz» (color ONE+ONE, alfa sin cambios), se regeneró el instalador (SHA más abajo), se reinstaló con la misma autorización y se comprobó en la app instalada que el anillo ya no aparece.
- Ciclo completo con el **servicio real**, en la app instalada (con el propietario de acuerdo en ceder la ventana):
  - Estado «Estado actualizado» y modo piloto. Perfil real «2» (1 regla de aplicación, 10 de sitios), preferencias reales y canales Servicio «Conectado» / Extensión «Disponible».
  - Se desactivó el bloqueo para la prueba. La revisión del servicio mostró perfil 2, 1 min, fin exacto 11:24:41 y «Sólo temporizador».
  - Confirmar e iniciar abrió la cabina inmersiva (00:00:51, «Modo normal», confirmación de bloqueo «No solicitado»). DrillClient: `Active`, la misma `sessionId e0c3fc51…`, `enforcementConnected=false`.
  - Detener sesión mostró el diálogo con el fin exacto. Tras confirmar: UI en reposo, aviso «Sesión interrumpida · Menos de 1 min · 2» y DrillClient `Stopped` con la misma sesión.
- Incidencia de método: `open_application` de computer use abrió una segunda instancia sin `--pilot`. Se cerró solo esa instancia (la abierta por el agente) y se trabajó con la ventana `--pilot` traída al frente por PID.

## Cuarta ronda: evidencias de los objetivos de UX

- **Apple design**: no hay ninguna skill con ese nombre (ni en el catálogo de claude.ai ni en `~/.claude/skills`). Las páginas HIG (Materials, Motion) se renderizan con JS y no se pudieron leer, así que se aplicaron sus principios conocidos y se dice así.
  - Hueco real corregido: con **Reduce Transparency** cada superficie de vidrio pasa a material opaco (`--glass` → `--surface`, sin `backdrop-filter`). Queda como contrato en `verify-dist`.
  - Ya se cumplían: el tinte solo en la acción principal, el movimiento con propósito, interrumpible (resortes) y sin animación con movimiento reducido.
- **Procrastinación**: chip de **5 min** en primer lugar (micro-compromiso para empezar). Prueba: el chip va primero y `session.prepareNormal` recibe `durationSeconds: 300`.
- **Medidas objetivas**: mismo host simulado, mismo script y viewport de 1280×800, contra la UI original (HEAD en un worktree temporal):

  | | Original (HEAD) | Rediseño |
  |---|---|---|
  | Palabras visibles en reposo, por encima del pliegue | 84 | 31 (−63 %) |
  | Palabras visibles con sesión activa | 99 | 48 (−52 %) |
  | Botones visibles con sesión activa | 4 (con la barra lateral) | 2 («Ver panel», «Detener sesión») |
  | Fondo de la pantalla de sesión | estático | `atmosphere-drift` 26 s; los campos se desplazan 93–128 px en 3 s |
  | Barra lateral | `background-image: none` | `linear-gradient(...)` del ambiente |

  La acción principal en reposo queda visible sin desplazar la página (y = 602 de 800).
- **Comparación con Opal** (de memoria de producto, sin capturas de Opal):

  | Opal | FocusGuard |
  |---|---|
  | Pantalla de sesión a pantalla completa | Cabina que oculta barra lateral y cabecera |
  | Fondo degradado en movimiento | Tres campos de color difuminados que derivan |
  | Temporizador central grande | Orbe con reloj y anillo de progreso |
  | Una frase calmada | «Respira. Solo esto ahora.» |
  | Pocos controles | Dos botones |

  Diferencia deliberada: en modo estricto la cabina mantiene visibles la insignia y la ruta de recuperación (THREAT_MODEL/UX_SPEC).
- **Sin prueba con usuarios**: las cifras anteriores son mediciones heurísticas, no un test con personas.
- La preferencia «Activar bloqueo por defecto» del propietario sigue **activada** (leída del servicio real en la pantalla Preferencias instalada); la prueba de sesión no la cambió.

## Quinta ronda: fuentes reales en vez de memoria

- **Apple HIG (texto real)**: obtenido del JSON de documentación de Apple (`/tutorials/data/design/human-interface-guidelines/materials.json`), porque la página web se renderiza con JS.
  - Citas aplicadas: «Don't use Liquid Glass in the content layer», «use standard materials for elements in the content layer», «Use Liquid Glass effects sparingly» y «Thicker materials… provide better contrast for text».
  - Cambio: el vidrio (translucidez + brillo) queda solo en controles y navegación. El contenido usa `--material`, un material estándar más grueso (84–86 % opaco, sin brillo). Comprobado con estilos computados: `.pref-group` en `rgba(22,20,31,.84)` sin `box-shadow`; las píldoras de cabecera siguen siendo vidrio.
- **Opal (fuente real, opalapp.com)**: «Choose what you want to focus on, set the length, and lock in until the timer runs out» coincide con nuestro flujo (objetivo → duración → revisar → cabina hasta el final). Su lenguaje visual son gemas con halo luminoso, al que responde el orbe con halo.
  - «Hard Mode» («No way out») equivale a nuestro modo estricto, que aquí es recuperable a propósito (THREAT_MODEL).
  - Las «Focus Gems» (recompensas por rachas) no existen en FocusGuard; quedan como propuesta, no se añadieron sin especificación.
- **ui-ux-pro-max aplicado como prescribe la skill** (design system persistido, Master + overrides por página): `docs/design-system/focusguard/MASTER.md` y `pages/focus-cockpit.md`, con lo adoptado y cada override justificado (paleta, tipografía, efectos, materiales).
- .NET 921/921 (14 proyectos) tras los cambios de UI de las rondas 4–5; React 168/168.

## Sexta ronda: prueba del propietario en la app instalada

El propietario probó la app instalada y respondió: inicio «confuso». Las opciones estaban debajo del botón principal. El formulario era editorial y desalineado, sin jerarquía y con selectores de distinto estilo. La página se movía al cambiar Temporizador/Ciclos. Las tarjetas de canal eran poco claras y los selectores de color, idioma y tema no pertenecían a esta pantalla. La confirmación no tenía fondo y su botón era distinto. En la cabina: los segundos bajaban de 4 en 4, sobraba el texto del perfil, quería ver las apps bloqueadas apiladas y los sitios contados, quitar las líneas del orbe y que nada apareciera de golpe. Cambios:

- **Inicio**:
  - Orden orbe → tarjeta de ajustes → **barra CTA fija abajo** (`position:sticky`; las opciones se leen antes de la acción).
  - La tarjeta es una lista alineada: etiqueta a la izquierda y control a la derecha, con tipo segmentado, perfil, duración, «Bloquear distracciones» y modo estricto como interruptores, y objetivo con placeholder. Mismo tamaño y peso por rol; la advertencia de trabajo sin guardar en color de aviso.
  - La altura de la tarjeta se anima (`AutoHeight` con ResizeObserver) y la fila de chips conserva su alto en modo ciclo. **Medido: sin desplazamiento**: orbe y = 250, chips 40 px y formulario y = 580 en ambos modos.
- **Cabecera**: ambientes, idioma y tema salen de la cabecera (viven en Preferencias); solo se muestran sin dashboard (modo de solo lectura, donde Preferencias no existe).
- **Checkout**: tarjeta con material, insignia mini-orbe con la duración, «Terminas a las …», líneas con separadores, alcance del bloqueo resaltado y el **mismo botón principal** que «Revisar sesión», con «Volver» como acción secundaria.
- **Canales**: chips de vidrio con icono SVG (enlace, globo, candado), estado por color (`--success`) y palabra, sin párrafo; el detalle queda en la vista Conexión.
- **Cabina**:
  - **Bug de segundos corregido**: `useLiveRemaining` cuenta segundo a segundo entre sondeos, se resincroniza con cada lectura del servicio y se congela si la lectura no está confirmada. Medido: 24:18 → 24:12 en 6 s consecutivos.
  - Perfil = solo nombre, más `BlockedStack`: hasta 5 círculos de apps superpuestos (icono o inicial) con «+N», y «N sitios bloqueados».
  - Sin hilos orbitando en el orbe.
- **Coreografía**: transición de vista (fundido cruzado de 0.5 s) al entrar y salir. El orbe **crece** (escala 0.6 → 1 con desenfoque) y después entran en orden la frase, el fin, el perfil, los controles y los canales (retrasos de 0.45 a 0.85 s). Medido: a 50 ms el orbe está en opacidad 0 y escala 0.6; a 400 ms en 0.92/0.97 con los controles aún en 0; a 900 ms los controles en 0.6; todo completo a 1.5 s. Con movimiento reducido no hay transición.
- Pruebas: React 172/172 (4 nuevas: cuenta atrás segundo a segundo con resincronización y congelada si no está confirmada, cabecera sin controles de apariencia con dashboard y chips con icono y estado, pila con +N y sitios, carga de reglas/ocultación). .NET 921/921.

## Séptima ronda: segunda prueba del propietario

Comentarios: demasiado texto en el formulario; no se ve qué se bloquea; el formulario de ciclos es pésimo; perfiles y ciclos como tarjetas deslizables; Bloquear y Estricto en una fila; chips con poco espacio entre nombre y estado; destello negro al pasar el puntero; mm:ss sin horas; en «Ver panel» los chips iban a la derecha; botones no redondos; anillo de foco blanco molesto. Cambios:

- **Ayudas en tooltips** (`InfoTip`):
  - Los textos secundarios (consecuencia del modo estricto, alcance o aviso del bloqueo, diario local del objetivo) van tras un «i». Se muestran al pasar el puntero (retraso de 0.2 s), con el foco de teclado o al tocar, y se cierran con Esc.
  - Siguen en el DOM como `aria-describedby`.
  - El «i» queda fuera del `<label>` (que apunta con `htmlFor` al interruptor) para no convertirse en el control etiquetado.
  - Las consecuencias completas siguen en la revisión y en el panel estricto.
- **Perfiles como tarjetas** (radiogroup con desplazamiento horizontal y scroll-snap): monograma con acento, nombre y número de apps y de sitios bloqueados.
  - Debajo, para el perfil elegido: iconos de apps apiladas (+N) y los **nombres de los primeros sitios** (youtube.com, x.com… +N), cargados de las reglas guardadas sin ir a Perfiles.
- **Duración** con stepper (− 25 +, paso 5, número tecleable). **Bloquear y Estricto** en una fila (dos mosaicos con interruptor; el estricto activo se tiñe de aviso).
- **Ciclos**:
  - Presets como tarjetas con una minilínea de tiempo.
  - «Personalizar este ciclo» con mosaicos de color (enfoque, descanso corto, descanso largo, cada N), steppers, segmentado «Número de ciclos | Duración total» y **línea de tiempo en vivo con el total** (p. ej. 4×25 + 3×5 = 1 h 55 min).
  - La lógica de validación y envío no cambió.
- **Cabina**:
  - Orbe en **mm:ss** por debajo de una hora (`formatClock`); h:mm:ss desde 1 h.
  - Chips con 4 px entre etiqueta y valor.
  - En «Ver panel» los chips quedan **debajo**. Decisión del propietario: el contrato de `verify-dist` cambia de «al lado en escritorio» a «debajo en todo ancho».
- **Global**:
  - Hover del botón principal sin destello: mantiene el degradado con `brightness(1.05)`. Medido: degradado presente y fondo transparente en `:hover`.
  - **Todos los botones son píldora**.
  - Anillo de foco suave: acento al 55 %, solo `:focus-visible`, oculto en destinos de foco programático (`tabindex=-1`); los campos usan un halo en vez del contorno.
- Verificado en la vista previa: cero desplazamiento al cambiar de modo; el tooltip abre, cierra con Esc y no activa el interruptor; reloj «15:57»; chips debajo del panel.
- Pruebas: React 178/178 (6 nuevas: `formatClock`, InfoTip, Stepper con ajuste a la rejilla y límites, tarjetas de perfil → `profileId` enviado, interruptores en fila con consecuencia en el tooltip, ciclo personalizado con steppers → configuración enviada y total). .NET 921/921.

## Octava ronda: tercera prueba del propietario

Antes de los cambios, el propietario ya tenía en marcha una sesión de ciclo («Deep Work», más de 1 h 50 min). No se inició ni se detuvo nada. La captura de la app instalada, contra el servicio real, confirmó la cabina, el reloj h:mm:ss por encima de una hora, la tarjeta de fase y los chips. También mostró la línea de color de la fase y el aviso de «saltar descanso» sin centrar. La reinstalación esperó a que esa sesión terminara (`Stopped`).

- **Iconos con estilo orbe** en tarjetas de perfil y de preset (perla con el acento del perfil o del ambiente). Presets con glifo: 🍅 Pomodoro clásico, 🌊 Enfoque profundo y 🌀 Flujo extendido (por id integrado, o por el nombre como respaldo); ⚡ para sprint o rápido.
- **CardSlider**: sin barra de desplazamiento visible; flechas de una tarjeta y puntos que siguen la posición y llevan a cada tarjeta. Solo aparecen si hay desbordamiento. Sigue siendo un radiogroup nativo.
- **Personalizar este ciclo**:
  - Las barras animan su anchura (transición de `flex-grow`) y entran con escala.
  - **Resaltado bidireccional** entre mosaicos y barras: verificado con puntero real. Sobre «Descanso corto» se activan las 3 barras cortas y el mosaico; sobre una barra de enfoque se activa el mosaico de enfoque y los descansos bajan a opacidad 0.28.
  - «Finalizar por» une el segmentado y su contador en un solo bloque.
  - **Contadores del mismo ancho** (5 × 172 px medidos).
- **Orbe de ciclo**: el anillo dibuja cada fase como segmento (enfoque con el acento, descanso corto `--orb-c`, descanso largo `--warning`), atenúa lo transcurrido y marca el ahora con un punto (`ringSegments` en `cycle-schedule.ts`, compartido con la línea de tiempo).
- **Cabina de ciclo**: sin la línea de color en «Fase actual» y con «Sólo puedes saltar el descanso actual…» centrado.
- Pruebas: React 183/183 (nuevas: planificador de ciclos con descanso largo cada N y corte por duración total, fracciones del anillo sin huecos, glifos de preset, anillo segmentado con lo pasado atenuado y el ahora marcado). El host simulado ganó los 3 presets integrados y `?state=cycle`.

### Verificación instalada con bloqueo real (autorizada: «Start it yourself»)

Ya con la ronda 8 instalada:
- **Inicio**: el slider de perfiles reales con flechas y puntos (2, Deep Work, Deep Work2, gpt…), iconos orbe y la vista previa del perfil «2» (app «F» · twitter.com · x.com · twitch.tv · +6).
- Sesión de 1 min **con bloqueo**. **Checkout** del servicio: perfil 2, 1 min, fin exacto, alcance «1 aplicación · 9 sitios» y el aviso de trabajo sin guardar.
- **Cabina**: reloj **00:49** (mm:ss); nombre «2»; pila con la app bloqueada (inicial «F»: esa regla no tiene icono de catálogo); «9 sitios bloqueados»; chips Servicio Conectado · Extensión Disponible · Confirmación de bloqueo **Confirmada**.
- DrillClient: `Active`, `enforcementConnected: true`. Detener sesión dio `Stopped` con `enforcementConnected: false` (bloqueo liberado).
- Ajuste detectado y corregido: el mensaje transitorio «Acción aceptada…» salía alineado a la izquierda; ahora está centrado.

### Incidencia: reinstalación rechazada por la UI abierta

La reinstalación de ese ajuste falló **de forma segura**: «FocusGuard UI process … did not close; no files were replaced». El servicio siguió Running con `serviceReady` y la reversión dejó el estado anterior. La instancia de la UI la había abierto el agente y no responde a `CloseMainWindow`. Se cerró ese proceso (sin sesión activa; la sesión vive en el servicio) y la instalación siguiente terminó con exit 0, servicio Running, Desktop restaurado y `host.js`/`host.css` idénticos a `dist/`.

## Skill «apple-design» (Emil Kowalski, ui-skills.com), aportada por el propietario

El propietario pasó el enlace https://www.ui-skills.com/skills/emilkowalski/apple-design. Se leyeron sus 64 reglas y se auditó la app contra ellas.

- **Ya cumplía**:
  - Respuesta en pointer-down; arrastre del dial 1:1 con `setPointerCapture`.
  - Solo `transform`/`opacity` en el movimiento.
  - Tooltips con origen en su disparador y unos 10 px de alcance extra.
  - Validación en línea; sin superficies translúcidas apiladas.
  - Reduce Transparency → opaco; Reduce Motion → sin movimiento.
- **Corregido**:
  - #11–14: resortes de UI sin momento con `bounce: 0, duration: 0.4` (píldoras y segmentados tenían 0.12–0.15; el asa del dial al agarrar tenía 0.3).
  - #1: presión `scale(0.97)` a 100 ms ease-out.
  - #6: fundido de la cabina acortado a 0.35 s.
  - #34: jerarquía de desenfoque (chips 12 px, barras flotantes 32 px).
  - #36, #47 y #48: tracking ligeramente positivo en texto pequeño, interlineado ajustado y tracking negativo en títulos.
  - #45: el cambio claro/oscuro se suaviza en 0.35 s.
  - #44: **`prefers-contrast: more`**, con superficies casi sólidas y bordes definidos. Queda como contrato en `verify-dist`.
- **Conflicto deliberado**: la #45 desaconseja fondos animados a pantalla completa, pero el brief del propietario los pide. Se mantienen muy lentos (un ciclo cada 26–64 s, lejos de 0.2 Hz) y desactivados con movimiento reducido.
- La #53 (confirmación solo en lo destructivo) cede ante SPEC/UX_SPEC: toda acción de bloqueo debe mostrar su hora exacta de fin antes de confirmarse.
- React 183/183. Instalador regenerado e instalado (SHA más abajo).

## Evidencia primaria: apple-design completo y Opal lado a lado

- **apple-design**: se leyó el **texto completo** de la página (navegador integrado, `get_page_text`), no un resumen. Además de la auditoría anterior, se aplicaron:
  - §12 + playbook «Fade the edges of scrollable lists»: `mask-image` en los bordes del slider cuando desborda.
  - «Materialize, don't just fade»: diálogos y checkout entran con desenfoque y escala a la vez, con fundido simple si hay movimiento reducido.
- **Opal** (opalapp.com, pantalla Timer, capturada en el navegador integrado) frente a FocusGuard:

  | Opal | FocusGuard |
  |---|---|
  | Reloj grande «30:00» (mm:ss) | Orbe con mm:ss (h:mm:ss solo con horas) |
  | Stepper «− 30m +» | Stepper «− 25 min +» |
  | Píldora «▶ Start» a todo el ancho | Píldora «Revisar sesión» a todo el ancho y fija; la revisión es obligatoria por SPEC (hora exacta de fin) |
  | Interruptor «Block Apps» | Mosaico «Bloquear distracciones» |
  | «For You»: tarjetas en carrusel horizontal | Sliders de tarjetas de perfil y preset |
  | Gemas con halo luminoso | Orbe perla con halo e iconos estilo orbe |
  | Fondo oscuro atmosférico | Atmósfera con degradados lentos, en oscuro y claro |

  Diferencias deliberadas:
  - «Hard Mode» de Opal frente a nuestro modo estricto, recuperable por THREAT_MODEL.
  - Las «Focus Gems» (recompensas) no existen aquí: quedan como propuesta.

## Verificación en la app instalada (no en la vista previa): fondo en movimiento y barra lateral

El propietario estaba usando la app instalada con una sesión real («Social media», 7 sitios bloqueados, confirmación de bloqueo «Confirmada») en la vista panel, y no se tocó nada.
- Dos capturas de la ventana instalada, con 6 s de diferencia, comparadas por píxeles en zonas solo de fondo: diferencia media de 4.7 (derecha) y 6.9 (abajo a la izquierda), con desplazamiento del color medio, de (27,36,45) a (31,43,53). **El fondo se mueve en la app instalada.**
- La **barra lateral con degradado** mide (77,82,93), claramente teñida frente al fondo principal (~26,31,41), y se mantiene estable.
- La misma captura confirma, en la instalación: chips debajo de la sesión en la vista panel, botón «Volver al foco», reloj mm:ss y «N sitios bloqueados».
- Uso real: el propietario inició por su cuenta sesiones de ciclo y de temporizador con bloqueo en la app instalada. Es uso, no una prueba de usabilidad formal; tras probar la 0.1.140 instalada, el propietario **aprobó explícitamente** el flujo («Sí, lo apruebo»: se entiende sin instrucciones y activar el bloqueo no tiene fricción), 2026-10-10.

## Incidencia: instalación interrumpida y recuperada

La sesión del agente se cortó durante la reinstalación de la .140 de esta ronda. El log terminaba en «Suspending FocusGuard browser native-host registration during update.». El servicio quedó **Stopped**, sin procesos FocusGuard, y con archivos de UI parcialmente copiados. Al retomar se reejecutó el mismo `install-focusguard.ps1` (idempotente, con la misma autorización): exit 0, health IPC autoritativo, servicio Running. Después se validó el paquete (`test-installer` OK, `--package-self-test` 0), los 8 archivos de UI instalados coinciden con `dist/` y DrillClient da `serviceReady/extensionReady` true e Inactive. La protección estuvo caída solo durante ese intervalo y no había sesión activa.

## Instalador

- `build-installer.ps1 -Version 0.1.140` (regenerado tras la segunda ronda): exit 0. `FocusGuard-0.1.140-Setup.exe` mide 443,107,512 bytes, SHA-256 `5E3AFAA2C30D9F5ADF66F59B78DD118C8901FCD39A7D59185A8DF4BC6DA1255B` (con apple-design completo: bordes difuminados y materialización; validado e instalado).
- El payload contiene los 14 chunks (incluidos `FocusOrbScene.js` y `jsx-runtime.js`); `host.js` y `FocusOrbScene.js` empaquetados son idénticos byte a byte a `dist/`.
- `test-installer.ps1`: OK (humo de lanzamiento interactivo, paridad Chrome/Edge). Setup `--package-self-test`: exit 0.
- Instalado con autorización del propietario (ver aceptación instalada).
- Host.Windows 164/164 tras corregir la prueba de ventana (ver tercera ronda).

## Ronda 9: Perfiles, Actividad y Programación reestructurados

Pedido del propietario: «rediseña perfiles, actividad y programación, sobretodo perfiles esta confuso, reestructura todo».

- **Perfiles** (`ProfileEditor.tsx`): galería de tarjetas (orbe, nombre, contadores de sitios/apps) con una tarjeta «Nuevo perfil» al final; cabecera del perfil con «Personalizar» (nombre, color, duplicar, eliminar); las reglas pasan a **pestañas** (Sitios web · Aplicaciones · Descansos, con contador) para ver un solo tipo a la vez. Pestañas con `role=tab`, flechas ←/→ y paneles `hidden`. Las aplicaciones detectadas se buscan directamente (`ApplicationsPicker inline`) y las ya añadidas se marcan «Añadida». Las explicaciones largas pasan a `InfoTip`.
- **Actividad** (`WorkflowViews.tsx`): navegación de día en píldora, dos cifras grandes, **franja del día** con cada fase en su color y **una tarjeta por sesión** (antes una fila por fase) con barra de fases, estado y «Ver resumen», que abre el resumen como **hoja lateral** (`dialog`, Esc/fondo cierran y el foco vuelve).
- **Programación** (`ScheduleEditor.tsx`): **semana de un vistazo** (L–D con píldoras por horario), tarjetas con la hora grande, puntos de días, perfil y modo, e interruptor `role=switch` que sigue pasando por la confirmación con consecuencias. Editor: nombre, hora grande + `Stepper` de duración, días como botones redondos, perfil en tarjetas, interruptores de bloqueo e inicio automático; el paso de revisión no cambia.
- Sin cambios en los clientes del bridge ni en las validaciones del servicio; misma revisión/confirmación para borrar, pausar y activar.
- Host de vista previa (`dev/preview-host.ts`, sólo desarrollo): horarios, actividad del día, resumen de sesión y catálogo de aplicaciones de ejemplo.

Evidencia: React **189/189** (6 pruebas nuevas: pestañas y flechas, tarjetas y creación de perfil, picker en línea con «Añadida», agrupación por sesión + franja + hoja, semana + interruptor revisado, días y perfil en el editor). `npm run build` + verify-dist OK. Revisado en vista previa oscuro/claro y a 390 px sin scroll horizontal (se corrigieron ticks solapados, cabecera de perfil y botones de programación en móvil). Instalador `FocusGuard-0.1.140-Setup.exe` regenerado: 443,127,992 bytes, SHA-256 `3EE4CA1303ADD0A6B1A57C6A02193B6B9EC19CD58B5AA0B051B8D579999450A1`; `test-installer.ps1` OK; `--package-self-test` 0. **No instalado**: el propietario tenía una sesión Active en ese momento.

- Regenerado de nuevo a petición del propietario (UI 189/189, build + verify-dist OK): `FocusGuard-0.1.140-Setup.exe` 443,127,992 bytes, SHA-256 `7A380F694669E63B11BC7780C49F0343A9478D21AC667ECF162761F44566E8F7`; `FocusGuard-0.1.140-win-x64.zip` 699,721,482 bytes, SHA-256 `3FDC4765F5524687E0D716733B965E6139C819F4DB02642986AAC79D0DC38638`. `test-installer.ps1` OK, `--package-self-test` 0. No instalado.

## Ronda 10: ajustes del propietario

- Píldora «Estado actualizado» movida al pie de la barra lateral, en una línea (refrescar como icono, texto accesible intacto).
- Perfiles: carrusel de una fila (`CardSlider` con `role=group`), selección con fundido (sin anillo deslizante); al cambiar de perfil el anterior queda atenuado hasta que llega el nuevo (sin parpadeo ni salto de scroll; medido: scroll 150 → 150 durante el cambio); paneles con fundido.
- Personalizar perfil compacto: nombre en píldora con guardar en línea + herramientas en la misma fila, colores en una fila (panel de ~190 → 132 px).
- Días del editor de horarios: círculos de 44×44.
- Foco: sin anillo grueso; con ratón no se ve nada y con teclado queda un halo de 1 px (forced-colors conserva Highlight).
- Popups con animación de salida (`exitDialog`, 170 ms, fundido + escala + backdrop) en eliminar perfil, horarios, presets de ciclo, controles de sesión, fin de sesión, revisión estricta y hoja de resumen; inmediato con movimiento reducido. Medido: opacidad 1 → 0 en 170 ms antes de desmontar.
- Flecha izquierda del slider de perfiles arreglada (desplaza una tarjeta desde la posición actual).
- Iconos de aplicaciones: el host sólo resuelve el icono de una regla guardada cuando ya tiene el catálogo de apps de esa ventana; la UI ahora lo precarga en segundo plano una vez (600 ms tras conectar) y relee el perfil visible. Sin cambios en el host.

Evidencia: React 190/190 (+1 prueba de `exitDialog`), build + verify-dist OK. Instalador regenerado en `artifacts/installer-r10/` porque `artifacts/installer/FocusGuard-0.1.140/` quedó inaccesible para la sesión sin elevación tras la instalación (no se tocaron permisos): `FocusGuard-0.1.140-Setup.exe` 443,132,088 bytes, SHA-256 `31FFD9BB5CEAC86D9ED24E96C3D80327E9DAFD4B3C564064D311F1A4E11E2DCE`; zip 699,726,913 bytes, SHA-256 `4D5D94198C890807FEEC0FF9D5AEB24FF33717FB8B513599730F02513AE49C7B`. `--package-self-test` 0, `test-installer.ps1` OK, UI empaquetada idéntica a `dist/`. Pendiente: instalar (requiere al propietario con elevación) y verificar iconos en la app real.

## Ronda 11: Preferencias reestructuradas, revisión opcional y tres orbes nuevos

- **Preferencias** en tres pestañas (Apariencia · Sesiones · Al terminar una sesión); una página visible a la vez y explicaciones en `InfoTip`. Apariencia muestra el orbe en vivo junto al selector.
- **Revisar antes de empezar** (Sesiones): opción local (`focusguard.reviewBeforeStart`, activada por defecto). Desactivada, el botón dice «Empezar ahora»: la UI sigue pidiendo `session.prepareNormal/prepareCycle` al servicio (que valida todo) y confirma sola al recibir la revisión. Se muestra la revisión de todos modos si hay un aviso (bloqueo con sitios y extensión no lista, última fase recortada). **Las sesiones estrictas siempre se revisan** (consecuencias y hora exacta, AGENTS.md).
- **Orbes** (skill `motion-design`: respiración 0,98–1,02 cada 3–4 s, gas ×2 más lento, fluido viscoso ×1,5): `voice` (azul cielo con corrientes blancas que suben, borde lechoso, ondulación ×1,9), `lava` (corteza oscura con grietas incandescentes y halo de calor aditivo que respira), `cloud` (densidad translúcida en tres capas con bordes difuminados y ondulación de gas). Selección local (`focusguard.orb`, `html[data-orb]`); los materiales se reemplazan y liberan al cambiar. Halo y respaldo sin WebGL teñidos por estilo; en lava el temporizador usa tinta clara con sombra para ser legible.
- Evidencia: React 194/194 (+4: tabs/orbe/revisión en Preferencias, inicio directo, revisión forzada con aviso y estricta, almacenamiento). Verificado en vista previa: «Empezar ahora» pasa a la cabina sin mostrar revisión; los cuatro orbes en oscuro y nube en claro. Build + verify-dist OK.
- Instalador en `artifacts/installer-r11/` (la carpeta `artifacts/installer/FocusGuard-0.1.140` sigue sin acceso para la sesión sin elevación): `FocusGuard-0.1.140-Setup.exe` 443,136,184 bytes, SHA-256 `38DFA0FF6B253A120E47D23E3EDDB765D88A291BCF93BB28605601DD02674EDA`; `--package-self-test` 0; `test-installer.ps1` OK; `FocusOrbScene.js` empaquetado idéntico a `dist/`.
