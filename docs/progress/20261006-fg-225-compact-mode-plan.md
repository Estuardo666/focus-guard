# FG-225 — Modo compacto del host React

## Diseño

Desktop productivity tool de escritorio para leer el estado de una sesión finita de un vistazo. Mantener Geist, graphite/mint y los tokens semánticos existentes; variance 3, motion 2, density 6. La captura de atajos usa barra de título nativa Windows como superficie de arrastre; mantener entrada/salida inmediata, sin animación.

El modo compacto conserva: tiempo restante autoritativo, fase actual, perfil, fin de sesión y avisos de desconexión/protección/recovery. Incluye «Abrir vista completa», «Siempre visible» y alternancia Ctrl+Shift+M. La vista completa conserva su navegación. Cerrar la ventana no cambia ni detiene la sesión del servicio. La bandeja sigue perteneciendo a Classic durante el piloto.

## Plan técnico

1. Añadir comandos bridge tipados y acotados para compactar/restaurar y TopMost; validar origen, documento, parámetros exactos, límites y resultados. No acceder al cliente de sesión ni a Storage.
2. En MainForm, guardar/restaurar bounds, estado maximizado y mínimos; usar título/borde Windows nativo, mantener posición al compactar, publicar el estado a React al cambiar y tras navegar/recuperar renderer. Añadir Ctrl+Shift+M.
3. Añadir la tarjeta React compacta con tiempo/fase/perfil/fin, advertencias reales, recovery estricto y acciones accesibles; colores/tipografía existentes y `prefers-reduced-motion`.
4. Añadir pruebas de parser/bridge/ventana, proyección del estado, transición de vistas, fase, stale y aviso de bloqueo no confirmado; verificar teclas/targets/tamaño.
5. Ejecutar tests focalizados, test React, Release build, Graphify y regenerar/verificar instalador 0.1.73. No instalar sin token elevado.

## Criterios de aceptación

- Un click y Ctrl+Shift+M alternan compact/full y restauran bounds; el shortcut no se anima.
- El título nativo mueve la ventana; TopMost se puede activar/desactivar.
- El compacto nunca inventa tiempo ni estado; en stale/unavailable marca el valor previo y ofrece vista completa/refresh.
- Muestra hora, fase, perfil conocido o etiqueta neutral, fin exacto y avisos aplicables; strict muestra acceso a recovery en la vista completa.
- Cerrar el Host no emite stop; no se modifica ni se duplica la bandeja.
- React/C# validan comandos exactos; las pruebas no dependen de sólo CSS.

## Riesgos y límites

No se añade la bandeja ni notificaciones nativas; se evita que dos hosts reclamen el mismo icono. El smoke visual instalado y DPI multi-monitor requerirán la aceptación instalada correspondiente; el entorno actual debe verificarse antes de declarar esa cobertura.