# Migración del workspace a D: — 2026-10-13

## Alcance y plan

El propietario autorizó trasladar el proyecto desde `E:\FocusGuard\FocusGuard-Autopilot` a `D:\Users\Stuart\Documents\Portafolio\Trabajos\Totem\FocusGuard`.

Plan: comprobar destino vacío y capacidad, copiar íntegramente, verificar contenido e historial, adaptar rutas operativas, validar desde D: y retirar origen únicamente después de verificar.

## Resultado

- Robocopy `/E /COPY:DAT /DCOPY:DAT /XJ /R:1 /W:1 /MT:8`: exit1 (copia exitosa); cero fallos y cero diferencias. 81.566 archivos / 20.376.627.953 bytes, aproximadamente 18,98 GiB. El total exacto de bytes está en `20261013-migration-verification.json`.
- Comparación SHA-256 de todos los archivos fuente/destino: iguales antes de editar rutas. Mismo HEAD y mismo `git status --porcelain=v1 -uall`, incluidos cambios locales y archivos no versionados. Sin enlaces reparse en origen.
- Nueva carpeta de trabajo declarada en README y AGENTS. `Store-ResendSecret.ps1` resuelve el destino local relativo al repositorio; no se ejecutó ese script ni se expusieron secretos.
- Actualizadas rutas de las dos configuraciones INI del AVD guardado en el repositorio. AVD activo en C: conservado. Registros históricos conservan rutas originales.

## Validación

- Sintaxis PowerShell del script modificado: sin errores.
- `dotnet test tests/FocusGuard.Domain.Tests/FocusGuard.Domain.Tests.csproj -c Release --nologo`: 39/39 PASS desde D:.
- Android Gradle offline `:app:assembleDebug :app:testDebugUnitTest`: BUILD SUCCESSFUL desde D:, 41 tareas; unit tests NO-SOURCE, no se presentan como pruebas ejecutadas. Aviso de versiones XML del SDK y bibliotecas sin strip, sin fallo de compilación.
- Emulador 37.3.3 y Java 17.0.20.1 ejecutados desde D:.
- Instalador existente .158 `--self-test`: exit0, SHA256 `31BAA0B22EDD303CA43394B38B75B800FFFCC7EA38D90FA729EE63F93A571F62`. Ningún componente distribuido cambió; no se regeneró instalador.
- `git diff --check`: sin errores, solo avisos LF/CRLF preexistentes.
- No hay variables de entorno persistentes User/Machine que referencien la antigua ruta.
- `graphify update .`: exit0; grafo actualizado desde D: (22.551 nodos, 139.922 aristas); visualización HTML omitida por superar su límite de tamaño.

## Pendiente

La revisión automática rechazó `Remove-Item -LiteralPath 'E:\FocusGuard\FocusGuard-Autopilot' -Recurse -Force`. No se ejecutó la eliminación ni se intentó evadirla. **La copia de E: permanece y sus 18,98 GiB todavía no se liberaron.** D: es el destino validado y designado para trabajo futuro; no mantener desarrollo divergente en E:.

Codex todavía registra FocusGuard en E:; abrir/asociar la nueva carpeta en la aplicación queda pendiente. No se manipularon las bases de datos internas de la app. No se cambiaron servicios instalados, cuentas, cloud ni datos de usuario. Espacio libre observado al validar: D: 90,23 GiB, E: 100,95 GiB.

Próximo paso: abrir la nueva carpeta en Codex y retirar el origen mediante un procedimiento autorizado que permita la política. Pruebas físicas de Fase 2 continúan pendientes.
