# FG-381 — aceptación de Perfiles a 800×600 (read-only)

## Resultado
La UI instalada .132 conserva la ruta Perfiles al reducir la ventana a 800×600. El selector nativo se mantiene arriba; la lista de cinco perfiles queda en dos columnas; las reglas de aplicación y de sitios se organizan en paneles apilados que continúan con scroll vertical, sin recorte horizontal.

## Evidencia y alcance
- Servicio FocusGuard: Running como LocalService. Host `FocusGuard.UI.exe --pilot`, usuario medio, vuelve a Resumen a 1216×799 al terminar; Servicio Conectado, Extensión Disponible, Sin sesión activa.
- Perfil seleccionado `2`; las fichas y conteos de reglas permanecen visibles. Expandí nombre/color para inspección; UIA expuso nombre, colores y Renombrar/Duplicar/Eliminar. No cambié campos ni activé esos botones; no se creó perfil, no se añadieron/quitaron reglas ni se guardaron cambios.
- `desktop-settings.json` permanece con el LastWrite de 10/9/2026 1:19 PM.
- Capturas: `D:\TEMP\FocusGuard-FG381-profiles-viewport-20261009\installed-profile-list-800x600-0.1.132.png` y `installed-profile-rules-800x600-0.1.132.png`.

Esto acepta el layout read-only de una ruta en ancho estrecho; no demuestra equivalencia de edición/guardado ni de las demás rutas. La siguiente prueba debe cubrir edición de perfil/reglas en una ventana estrecha sin alterar datos persistentes.