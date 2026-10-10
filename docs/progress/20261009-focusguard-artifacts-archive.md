# FocusGuard — archivo recuperable de artefactos generado en E:

El 2026-10-09, ante la solicitud de liberar espacio del repositorio, moví únicamente salidas generadas y cachés dentro de `E:\FocusGuard\FocusGuard-Autopilot` a un archivo recuperable en `D:\TEMP\FocusGuard-E-artifacts-archive-20261009`.

- Primera pasada: 17,394 archivos / 53.008 GiB desde 23 rutas de `artifacts`, incluidas `artifacts\installer` (20.47 GiB), `setup-publish` (12.79 GiB), `setup-payload` (9.12 GiB), versiones Setup y ZIP.
- Segunda pasada: 38,561 archivos / 31.874 GiB; 20 salidas `fg*-output` (26.17 GiB) y 76 cachés .NET `bin`, `obj`, `TestResults` (5.70 GiB). El paso 2 omite `artifacts\installer` por estar ya archivado en la primera pasada.
- Total registrado: 55,955 archivos, 91,141,367,262 bytes (84.882 GiB / 91.141 GB decimales). El directorio del archivo y los manifiestos siguen presentes en D:.
- El inventario `inventory.csv` asigna cada `OriginalEPath` a `ArchiveDPath`; el segundo manifiesto enumera las rutas y estados. `README.txt` explica la recuperación.
- No se borró permanentemente el contenido archivado. No se movieron archivos fuera del repositorio ni se tocaron perfiles o datos personales. Permanecieron en E el código fuente de producción, el código fuente de tests y la documentación; sí se archivaron salidas históricas de aceptación (recibos, JSON, UIA y capturas) junto a los binarios compilados y TestResults. Esta operación libera E y ocupa D; no libera D.

Lectura comprobada tras el archivo: E: 123.327 GiB libres (132.422 GB decimales); D: 25.88 GiB libres. Los artefactos nuevos que se generaron después de la limpieza permanecen en el repositorio.