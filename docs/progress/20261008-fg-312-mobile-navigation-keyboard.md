# FG-312 — Navegación móvil por teclado en Host .102

Fecha: 2026-10-08
Estado: completado en la instalación normal .102; no es una aprobación de accesibilidad completa.

## Prueba

FocusGuard.UI.exe .102 se ejecutó en modo Solo lectura, con sesión inactiva, en la ventana mínima720×480. Con el selector “Ir a la sección” y teclado (Alt+Down, flechas, Enter) se recorrieron:

- Perfiles → Perfiles y reglas
- Actividad → Actividad
- Programación → Programación semanal
- Preferencias → Preferencias
- Conexión → Estado de la conexión
- Ayuda → Enfoque sin perder el control
- Resumen → Tu espacio de enfoque

UIA confirmó que cada opción quedó seleccionada y su encabezado/contenido se renderizó. El selector volvió a Resumen. No se editaron datos ni se iniciaron sesiones. La ventana se restauró a1216×799; el Host siguió en Solo lectura y el servicio mantuvo Inactive.

## Alcance

Esto verifica el selector móvil y la carga de las siete vistas. No verifica el recorrido de Tab dentro de cada página, formularios, Narrator, forced-colors, DPI ni Windows11.

Evidencia UIA: artifacts/fg305-output/fg312-mobile-navigation-keyboard-0.1.102.json.
