# FG-227 — Acceso al piloto React desde la bandeja Classic

## Diseño

La bandeja sigue siendo el punto estable mientras Avalonia y React conviven. Añadir una acción explícita `React pilot` junto al modo compacto existente; el texto y la ayuda explican que la nueva interfaz es opcional y Classic permanece disponible para volver. No crear otro icono ni cambiar el destino del botón principal `Open FocusGuard`.

## Plan técnico

1. Agregar `React pilot` al menú nativo y al flyout de bandeja, usando recursos ES/EN y targets existentes.
2. Lanzar el `FocusGuard.UI.exe` hermano desde el directorio de instalación con `UseShellExecute=false` y el argumento fijo `--pilot`; no pedir elevación ni aceptar una ruta o argumentos desde la página.
3. Detectar el mismo ejecutable en la sesión actual y activar su ventana si ya está abierto, evitando duplicados. Notificar con un toast Classic localizado si el Host no está en esa instalación o no se pudo iniciar.
4. Añadir pruebas de construcción de `ProcessStartInfo`, proceso existente, host ausente y fallo de inicio; build/tests y Setup independiente 0.1.75.

## Criterios de aceptación

- Classic sigue siendo el único propietario del tray y conserva su opción normal `Open FocusGuard`.
- La entrada nueva describe el modo piloto e inicia sólo el ejecutable hermano verificado, a nivel de usuario normal, con `--pilot` fijo.
- Si existe ese mismo Host en la sesión, se reactiva en vez de abrir otro.
- El proceso de lanzamiento no envía comandos al servicio ni cambia perfiles/sesiones.

## Límites

La verificación de instalación autenticada, UAC, store extension, browser block y visual multi-DPI continúan en sus gates independientes. Este launcher mejora el descubrimiento del piloto, no lo hace predeterminado.
