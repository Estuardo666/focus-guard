# FG-309 — Tab y foco del Host .102 en viewport mínimo

Fecha: 2026-10-08
Estado: comprobación instalada completada; la cobertura total de accesibilidad permanece abierta.

## Alcance y resultado

El Host FocusGuard.UI.exe .102 se ejecutó como usuario normal, en SOLO LECTURA y con el servicio inactivo. Se redimensionó la ventana exterior a720×480 (área de cliente704×441), se enfocó el Host y se enviaron diez Tab.

Secuencia observada: Vista compacta → Idioma → Apariencia: Claro → Actualizar estado → Resumen → Ir al contenido → Ir a la sección → ciclo de navegación. UIA confirmó que Ir al contenido obtuvo el foco de teclado, IsOffscreen=false y bounds dentro del viewport. PrintWindow muestra el anillo visible en el enlace. Revisar sesión permaneció visible, deshabilitado y fuera del recorrido de teclado; su explicación permaneció visible. La ventana se restauró a1216×799.

## Estado y límites

El Host informó Servicio Conectado, Extensión Disponible y Sin sesión activa; el servicio siguió PID25096 bajo LocalService. No se activó ni detuvo una sesión y no se cambió configuración. Esto cubre la primera vista mínima y un recorrido parcial, no las siete páginas en .102, formularios, modo compacto, Narrator, forced-colors, DPI o Windows10.

Captura: artifacts/fg305-output/fg309-installed-0.1.102-focus-visible-720x480.png; secuencia y bounds UIA: artifacts/fg305-output/fg309-installed-0.1.102-tab-sequence.json.

## Plataforma verificada

Win32_OperatingSystem informa Windows 10 Pro 22H2 (10.0.19045), 64-bit, monitor principal 1920×1080. GetDpiForWindow del Host .102 devuelve96 DPI (100%); SystemParametersInfo informa High Contrast Off. Esto verifica sólo esta instalación/monitor; no acredita Windows11, otros DPI ni forced-colors.

## Etiqueta de perfil observada

La lista instalada contiene un perfil cuyo nombre guardado es literalmente “2”; el componente React renderiza profile.name como etiqueta del selector. “Perfil: 2” no es un índice. No se renombró ni modificó el perfil.
