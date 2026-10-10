# FG-357 — Fin del horario visible en la revisión

## Contexto

FG-355 dejó que un horario de 1 minuto continúe siendo válido. Si el worker llega tarde, la sesión solo usa el tiempo restante y conserva el `EndUtc` programado. El campo de duración ya explicaba que la sesión puede ser más corta, pero esa información desaparecía en el paso final de **Revisar**. El test del editor también tenía un título obsoleto que afirmaba que los horarios de 1 minuto no podían iniciar.

## Cambio de diseño

- La revisión final ahora dice en español e inglés que un inicio tardío usa el tiempo restante y mantiene la hora final programada.
- El formulario, el mínimo de 1 minuto, el orden de campos y el scheduler del servicio no cambian.
- React no calcula la próxima ocurrencia ni su zona horaria; el servicio conserva la autoridad del calendario y el fin.
- No se añade una alerta roja, animación ni otra decisión a la confirmación.

| Antes | Después | Por qué |
| --- | --- | --- |
| El helper de Duración explicaba el inicio tardío, pero el resumen final solo pedía confirmar perfil y horario. | El último paso repite el comportamiento de tiempo restante y fin programado. | La consecuencia queda visible justo cuando el usuario confirma, sobre todo para un horario de 1 minuto. |

## Verificación

- React: 136/136 pruebas. El test de programación crea un borrador de 1 minuto, abre **Revisar**, verifica el copy EN/ES y confirma que no se envía `schedule.save` antes de la confirmación explícita.
- `scripts/build-installer.ps1` construyó Setup .126 y validó todos los hashes de recursos UI incluidos. `npm run build`/TypeScript/verify-dist pasan.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.126`: Chrome y Edge pasan.
- `scripts/test-installer.ps1`: pasa.
- Cinco smokes del Host .126 (`--smoke`, runtime faltante, renderer failure, renderer crash y window DPI): exit 0.
- ZIP: 1,732 entradas; `zipfile.testzip()` no encontró entradas dañadas.
- Setup SHA-256: `25D165A571CC58A166CDE035C75270D94A07634E217039C4C5ED291759B0D124`.
- ZIP SHA-256: `F4E08D1D6C8FABC8BA2900534F54FFD832746649B85CEA0C5FE066B300DAA1FC`.

## Instalación y siguiente paso

.123 sigue instalado y el Host abierto procede de `C:\Program Files\FocusGuard\FocusGuard.UI.exe`; .126 no se instaló ni se aceptó visualmente en el Host real. El empaquetado inicial en E se quedó sin espacio al intentar cerrar el ZIP. Se trasladaron los artefactos de esta compilación a `D:\TEMP\FocusGuard-0.1.126-fg357-package`, se generó y validó el ZIP allí; E recuperó aproximadamente 3.2 GB libres. La aceptación instalada del mensaje y del horario de 1 minuto sigue pendiente. Después, continuar Fase 9 con Edge actualizado, FG-354 sin host nativo previo, reboot/recovery, DPI/monitor, accesibilidad de Windows y carga sostenida. Avalonia permanece disponible y Fase 10 no se inicia.
