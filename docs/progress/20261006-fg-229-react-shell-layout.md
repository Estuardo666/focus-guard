# FG-229 — Corrección visual full/compact React

## Cambio

`CompactFocus` ahora se oculta con `hidden` mientras la ventana está en full view y se muestra sólo cuando el Host nativo entra a compacto. El compacto recibe una jerarquía visual explícita para identidad, acciones, estado, tiempo/perfil, detalles y avisos a 480×340 y 480×220 DIP. El encabezado completo separa “Vista compacta” del shortcut. Cuando el estado del servicio no está disponible, el dashboard explica la ausencia una vez y omite hechos de conexión no verificables.

No cambian IPC, comandos de sesión, tray ni persistencia. Sin `--pilot`, los Hosts de QA operaron en modo de consulta.

## Verificación

- React: 95/95 pruebas, incluye visibilidad semántica full/compact, error offline sin facts “Desconocido” y copy de compact disconnect.
- `npm run build`: typecheck, Vite y CSP/local asset verification pasan; main bundle 379.77 KB.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings/0 errors.
- `scripts/test-installer.ps1`: pasa.
- Setup `--package-self-test`: exit0. Manifest 0.1.76 31/31 archivos; ZIP menú1732 entradas.
- Setup SHA-256 `D43626C4DC3111903B30891A3343A16D648E5FC132D65E4AD0B7E387C973C9D2`; ZIP SHA-256 `8C13E50300F06D015165DB58960E478E5A52EB6AE778B314957B04934421B873`.
- Graphify: 10.155 nodos, 27.380 aristas, 986 comunidades.
- Capturas de Host0.1.76 desde copia temporal: full `D:\TEMP\focusguard-fg166-validation\artifacts\fg229\react-host-0.1.76-full.png` (1216×799); compacto `...\react-host-0.1.76-compact.png` (ventana exterior496×379; área cliente480×340). Las capturas usan estado de servicio desconectado porque la copia temporal no está autorizada como cliente instalado.
- Captura Profile de Host instalado0.1.71 en lectura: `D:\TEMP\focusguard-fg166-validation\artifacts\fg229\installed-0.1.71-profiles.png`; no se guardó nada y `FocusGuard` service siguió Running.

## Límites y siguiente paso

El Setup0.1.76 y el ZIP se generaron en `D:\TEMP\focusguard-fg166-validation\artifacts`; 0.1.76 no se instaló. Falta validación instalada del tray→React, estado del servicio/IPC, pantalla de perfiles 0.1.76 y DPI/per-monitor. La sesión de Codex comprobó `IsAdmin=False`; no se ejecutó instalador ni se intentó elevar.
