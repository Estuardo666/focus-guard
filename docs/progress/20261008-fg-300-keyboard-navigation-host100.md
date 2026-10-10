# FG-300 — Navegación por teclado de pestañas del Host instalado

Fecha: 2026-10-08
Estado: aceptado para navegación principal; auditoría total de teclado pendiente.

## Prueba
Sobre FocusGuard.UI .100 instalado, inicié desde el enlace accesible “Ir al contenido” y Tab alcanzó la pestaña “Resumen”. La UI Automation nativa de Windows identificó los focos dentro del renderer WebView2 (parte del árbol del Host). Por ser una pestañera vertical, probé flecha abajo y no derecha: se recorrieron Perfiles, Actividad, Programación, Preferencias, Conexión y Ayuda, y volvió a Resumen; flecha arriba desde Resumen volvió a Ayuda. Los siete elementos exponían role TabItem, nombres localizados y AutomationId Radix.

Antes y después, DrillClient reportó serviceReady/extensionReady, sesión Inactive y ningún SessionId. No se activó ni detuvo sesión, ni se editaron perfiles. La ventana de prueba cerró de forma normal.

## Evidencia y límite
Secuencia UIA completa en artifacts/fg298-output/fg299-keyboard-navigation-host100.json. Esto valida Tab hasta el tablist y navegación por flechas dentro de él en .100. No es un recorrido completo de controles del resto de páginas ni verifica visualmente el anillo en cada foco, Narrator, forced-colors, DPI o .101 instalado.

## Flujo de Resumen en modo piloto
Una segunda ventana normal instalada .100 se abrió con --pilot y serviceReady=true. Con UI Automation nativa y teclado real, Tab desde “Ir al contenido” alcanzó Resumen; el recorrido cíclico expuso 13 focos accesibles, incluidos tipos Button, CheckBox, ComboBox, Edit, RadioButton y Spinner. “Revisar sesión” se encontró con nombre accesible y Enabled=true. No se pulsó Enter ni se cambió ningún valor. El recorrido terminó al volver a la pestaña Resumen; el servicio siguió Ready/Inactive sin SessionId. La ventana se cerró normalmente. Los valores dinámicos no se guardaron; solo se registran tipos/estados en artifacts/fg298-output/fg300-session-form-keyboard-host100.json.

Este alcance verifica Resumen y navegación principal en .100; no cubre teclado de perfiles, actividad, horarios, preferencias/diálogos, lector Narrator, forced-colors, DPI ni el CSS .101 no instalado.
