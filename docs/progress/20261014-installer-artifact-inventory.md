# Inventario de artefactos de instalador — 14 de octubre de 2026

## Estado

Inventario inicial completado; el intento de limpieza fue rechazado por política antes de ejecutar. Seguimiento posterior del 14 de octubre: los objetivos `.164` seleccionados ya no aparecen en `artifacts/installer`; `.165` y `.166` siguen presentes. No se atribuye quién retiró `.164`; el agente no ejecutó otro intento de borrado.

## Candidatos antiguos

La .164 está supersedida por la .166 y no tiene referencias activas a sus rutas de paquete en la documentación. Revalidación actual: el árbol completo contiene 1.653 archivos (incluye el Setup interno, `publish/`, extensión, host nativo y scripts); junto con el Setup y ZIP externos ocupa 1.878.703.503 bytes (aprox. 1,75 GiB). El conjunto de cuatro objetivos previamente auditados para retirar —Setup y ZIP externos, Setup interno y `publish/`— suma 1.877.969.140 bytes. Los 28 archivos pequeños restantes del árbol suman 734.363 bytes y no formaron parte de aquel conjunto:

| Ruta relativa | Bytes | SHA-256 |
|---|---:|---|
| `artifacts/installer/FocusGuard-0.1.164-Setup.exe` | 371.512.232 | `74D98E6B274307A40CE2E138D428D79B97F48310B11F4357866033156B7B29CF` |
| `artifacts/installer/FocusGuard-0.1.164-win-x64.zip` | 552.308.879 | `F8A7CABFBE6E0F7D1FA0EC4742E724ABE914C1B3943AD4E64612CE644C152CD3` |
| `artifacts/installer/FocusGuard-0.1.164/FocusGuard.Setup.exe` | 371.512.232 | `74D98E6B274307A40CE2E138D428D79B97F48310B11F4357866033156B7B29CF` (duplicado del Setup superior) |
| `artifacts/installer/FocusGuard-0.1.164/publish/` | 582.635.797 | Directorio con 1.624 archivos publicados |
| `artifacts/installer/FocusGuard-0.1.164/` — archivos restantes, no incluidos en el conjunto previo de borrado | 734.363 | 28 archivos de extensión, host nativo, manifiestos y scripts |

Los paquetes `.164` no son un candidato para instalar. Se conservan `.165` para reproducir y comparar el error de endpoint de desarrollo y `.166` como versión aceptada en la PC del propietario. Antes de una futura limpieza conviene conservar este registro de hashes y la documentación del diagnóstico.

## Alcance y resultado

- Intento: retirar únicamente los cuatro objetivos .164 de la tabla, todos dentro de `artifacts/installer/` del repositorio oficial.
- Resultado de ejecución: `Remove-Item` rechazado por política (`blocked by policy`) antes de comenzar.
- Verificación posterior inicial: los tres archivos aún existían con los mismos tamaños y hashes; `publish/` tenía 1.624 archivos. No se intentó una vía alternativa.
- Revalidación posterior del 14-oct: el árbol .164 tiene 1.653 archivos y 954.882.392 bytes; los Setup y ZIP externos mantienen sus hashes registrados. Footprint total de .164: 1.878.703.503 bytes; conjunto previamente seleccionado para retirar: 1.877.969.140 bytes, quedando fuera 734.363 bytes de archivos pequeños del árbol. Espacio libre medido en las unidades montadas: C: 18.294.661.120 bytes (17,04 GiB), D: 113.146.662.912 bytes (105,38 GiB), E: 83.995.131.904 bytes (78,23 GiB). Las cifras anteriores C:/D: eran de una medición distinta; no se atribuye la diferencia a una limpieza concreta.
- El inventario actual de `artifacts/installer` contiene las versiones `.164`, `.165` y `.166`. Las notas anteriores sobre volcados `.dmp` y paquetes `.160`–`.163` necesitan un inventario actualizado antes de actuar sobre ellos.
- No se tocaron otras carpetas del proyecto ni las unidades C: o E:.

## Seguimiento de cierre — 14 de octubre de 2026

Una revalidación posterior confirma que la carpeta `FocusGuard-0.1.164` y sus dos archivos hermanos `FocusGuard-0.1.164-Setup.exe` y `FocusGuard-0.1.164-win-x64.zip` están ausentes; una búsqueda en todo `artifacts/installer` no encontró nombres `.164`. Las versiones `.165` y `.166` permanecen. No se realizó una operación de borrado desde este agente después del rechazo; no se atribuye la retirada a una persona o proceso sin evidencia.

Entre mediciones, D: pasó de 113.135.140.864 a 115.014.135.808 bytes libres (+1.878.994.944 bytes, 1,75 GiB). La diferencia es compatible con la retirada de los paquetes `.164`, pero no se atribuye exactamente a ella. Espacio actual: C: 15.411.699.712 bytes (14,35 GiB), D: 115.014.135.808 bytes (107,12 GiB), E: 83.995.131.904 bytes (78,23 GiB). Los datos AVD/Gradle de C: siguen intactos.

## Revalidación posterior a pruebas Android

Tras repetir las pruebas instrumentadas en el AVD portátil y apagarlo, el estado de `.164` permanece igual: 1.653 archivos y 1.878.703.503 bytes en total entre su árbol, Setup y ZIP. Espacio libre ahora: C: 16.149.483.520 bytes (15,04 GiB), D: 113.135.144.960 bytes (105,37 GiB), E: 83.995.131.904 bytes (78,23 GiB). Antes de iniciar el AVD se habían medido C: 17,04 GiB y D: 105,38 GiB; C: bajó aproximadamente 2,00 GiB durante las pruebas. El inventario de solo lectura muestra `C:\Users\Stuart\AppData\Local\FocusGuard\AndroidVM` con 4.057.171.763 bytes y `C:\Users\Stuart\.gradle\caches` con 1.243.481.882 bytes; ambos tienen archivos con hora de escritura durante la ejecución. Sin una medición anterior por carpeta no se atribuye toda la caída a una sola ruta. Son candidatos de investigación, no se borraron ni movieron; no se usó otra vía para eludir el rechazo de eliminación de `.164`.
