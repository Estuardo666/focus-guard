# Checklist de aceptación con el propietario presente (.138)

Pruebas que el agente no puede ejecutar solo:

- Reiniciar Windows corta la sesión del agente.
- Cambiar la hora del sistema, el DPI o activar Narrator modifican la configuración del sistema; las hace el propietario.
- Edge necesita la sesión y el consentimiento del propietario.
- Este equipo es Windows 10, así que Windows 11 requiere otro equipo o una VM.

Cada bloque indica lo que se observa y qué comando cierra la evidencia. Todos los comandos se ejecutan desde una PowerShell **ya elevada** por el propietario, en `E:\FocusGuard\FocusGuard-Autopilot`. Ningún script se eleva solo.

## 1. Reinicio de Windows con sesión normal (Fase 3)

1. En el Host (modo piloto), inicia una sesión normal (no estricta) de **10 minutos o más**.
2. Arma el drill:

   ```powershell
   .\scripts\arm-normal-session-reboot-drill.ps1 -Arm -OwnerPresent
   ```

3. Reinicia Windows con Inicio → Inicio/apagado → Reiniciar, con la sesión aún activa.
4. Tras iniciar sesión, completa el drill con el `CompletionCommand` que imprimió el paso 2:

   ```powershell
   .\scripts\complete-normal-session-reboot-drill.ps1 -RunId <RunId> -OwnerPresent
   ```

   Pasa si el servicio vuelve Auto/LocalService, la sesión **no** se restaura, hay exactamente un `SessionInterrupted` (`Stopped` en apagado ordenado, `ServiceTerminated` si fue un corte) y no queda journal.
5. Abre FocusGuard → Actividad: la sesión aparece como «Sesión interrumpida».

## 2. Reinicio de Windows con sesión estricta (Fase 3, drill existente FG-112)

1. Inicia una sesión **estricta** corta (unos 10 min) desde la UI revisada; guarda el código de recuperación.
2. Ejecuta `.\scripts\arm-fg112-production-reboot-drill.ps1 -Arm -OwnerPresent` y reinicia.
3. Ejecuta `.\scripts\complete-fg112-production-reboot-drill.ps1 -RunId <RunId> -OwnerPresent`. Pasa si se recupera la misma sesión estricta y se libera al expirar.

## 3. Cambios de reloj (Fase 3)

La lógica está cubierta por pruebas automáticas: retroceso y salto del journal, y reconciliación estricta con el reloj monotónico. Para la aceptación real:

1. Inicia una sesión normal de 10 min y anota la hora de fin que muestra el Host.
2. En Configuración → Hora e idioma, desactiva «Establecer la hora automáticamente» y retrasa el reloj **1 hora**.
3. Espera 30 s y comprueba:
   - El Host no muestra tiempo restante inflado.
   - `& 'C:\Program Files\FocusGuard\FocusGuard.DrillClient.exe' status` sigue con el mismo `sessionId`.
4. Adelanta el reloj **2 horas** (más allá del fin) y espera 10 s. La sesión debe pasar a `Expired`, y Actividad la muestra como completada sin minutos de más.
5. Reactiva la hora automática y verifica que el estado sigue coherente.
6. Repite los pasos 2–5 con una sesión estricta corta para comprobar que no se libera antes de tiempo con un salto hacia delante del reloj de pared. Si se libera, anota la hora exacta.

## 4. Edge real (Fase 4/7, FG-388)

1. Abre tu perfil habitual de Edge y ve a `edge://extensions`. Instala o activa FocusGuard desde `C:\Program Files\FocusGuard\browser-extension` si no aparece.
2. Inicia una sesión con un perfil que bloquee un sitio de prueba (por ejemplo `example.com`).
3. Navega a ese sitio: debe aparecer la pantalla de bloqueo, y Actividad registra el intento sin la query string.
4. Termina la sesión: el sitio vuelve a cargar.

## 5. DPI y escala (Fase 5)

1. En Configuración → Pantalla, cambia la escala a 125 % y después a 150 % con el Host abierto.
2. Comprueba que no hay recortes, texto borroso ni controles fuera de la ventana, también en la vista compacta (`Ctrl+Shift+M`).
3. Si tienes un segundo monitor con otra escala, mueve la ventana entre monitores.
4. Restaura la escala original.

## 6. Narrator, teclado y alto contraste (Fase 5/6)

1. Activa Narrator (`Ctrl+Win+Enter`). Recorre el Host solo con teclado: Tab, Shift+Tab, flechas en la navegación, Enter y Espacio.
   - Debe leerse el nombre de cada control, el estado de la sesión y la advertencia del modo estricto.
   - El aviso «La sesión se interrumpió» se anuncia como alerta.
2. Abre y cierra con teclado los diálogos de revisar sesión, revisión estricta y resumen; el foco vuelve al control que los abrió.
3. Activa un tema de contraste (`Alt+Shift+Impr Pant`): bordes, foco y avisos siguen visibles.

## 7. Windows 11

Repite los bloques 1, 5 y 6 en un equipo o VM con Windows 11 e instala la misma `FocusGuard-0.1.138-Setup.exe` (SHA `8525B81D…C56E`).

## Registro

Por cada bloque guarda la salida de los comandos y las capturas en `D:\TEMP\FocusGuard-owner-acceptance-<fecha>\`, y avisa al agente para registrar el checkpoint.
