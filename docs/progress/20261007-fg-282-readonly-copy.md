# FG-282 — Aclarar el modo de solo lectura

## Hallazgo real

Inspeccioné el Host React .94 instalado con Orca a1216×799. En español, el carril decía “Vista de consulta”, mientras que la cabecera decía “Configura qué bloquear y qué permitir”; además, crear/editar estaba deshabilitado y el único motivo era “La edición está disponible en el piloto opcional.” La pantalla mezclaba una promesa de configuración con una restricción y no decía qué hacer después.

Lectura de diseño: panel de productividad local para personas que necesitan reconocer rápido qué pueden hacer en esta ventana y qué hacer cuando una acción no está disponible. Conservé la jerarquía y el sistema visual existente; corregí el lenguaje de estado y de acción.

| Antes | Después | Por qué |
| --- | --- | --- |
| “Vista de consulta” / “Read-only view” | “Solo lectura” / “Read-only” | Nombra el estado sin eufemismos y distingue la UI de la función de consulta. |
| “Configura qué bloquear y qué permitir” | “Consulta las reglas guardadas de cada perfil” | Evita prometer edición cuando el piloto está desactivado y sigue siendo verdad cuando está activo. |
| “La edición está disponible en el piloto opcional” | “Esta ventana está en modo de solo lectura. Para editar, abre FocusGuard en modo piloto; FocusGuard clásico también está disponible.” | Explica por qué los controles no responden y da una continuación concreta. |
| “El inicio está disponible en el piloto opcional” | “El inicio está desactivado en esta ventana. Abre FocusGuard en modo piloto o continúa en FocusGuard clásico.” | El motivo aparece junto a la acción primaria deshabilitada, no sólo en Ayuda. |

La pauta de UI/UX Pro Max sobre disabled states pide distinguir con claridad elementos no interactivos. Taste y frontend design favorecen leer el contexto real y ajustar el producto existente en vez de imponer otra plantilla; por eso conservé tokens y composición y corregí el mensaje. Emil y Apple recomiendan no añadir movimiento a acciones frecuentes sin necesidad; aquí la señal es persistente y de comprensión, así que no agregué animación ni efectos.

## Cambios y validación

Los mensajes de modo, perfiles, inicio, edición, controles activos, acciones de capturas y Ayuda se alinearon en español e inglés. Las regresiones nuevas fallaron primero sobre el copy anterior; tras el cambio, React112/112 y los cinco archivos de pruebas focalizados46/46 pasan. TypeScript, build Vite, CSP, recursos locales, verificación de contraste, extensión19/19 y `scripts/test-installer.ps1` pasan.

Se generó el paquete acumulado .96; incluye también FG-280. Sus 31 archivos del manifest existen, los 18 recursos UI coinciden por SHA-256 con `dist`, los cuatro smokes del Host publicando salen con código0 y los ZIP Chrome/Edge coinciden con la distribución (14 entradas cada uno). Recibo completo: `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/build-receipt-0.1.96.json`; smokes: `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/focusguard-host-smokes-0.1.96.json`.

El Service instalado .94 se consultó con DrillClient después de los smokes: Running como LocalService PID9308, serviceReady/extensionReady/Inactive y SessionId nulo. En la muestra estaban abiertos el Host React .94 PID27136 y Desktop Classic PID19972; ProcessAgent PID46048 seguía como hijo de Classic. No se inició/terminó sesión ni se cambió ningún perfil.

## Límite y siguiente paso

El paquete .96 está preparado pero no instalado. El token actual no está elevado; requiere el flujo normal de Setup/UAC. La captura de antes `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/installed-profile-readonly-before-0.1.94.png` documenta el copy anterior y contiene nombres de perfiles del usuario. FG-281 instalará .96 y revisará el estado con Orca/WebView2 sin editar reglas ni iniciar sesiones. Ese gate visual instalado sigue pendiente.
