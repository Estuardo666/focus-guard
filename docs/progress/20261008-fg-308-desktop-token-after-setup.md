# FG-308 — Token interactivo de Desktop tras Setup .102

Fecha: 2026-10-08
Estado: investigación abierta; runtime corregido manualmente, causa de arranque pendiente.

## Síntoma y evidencia

Después de instalar Setup .102, se observó FocusGuard.Desktop PID17992 y su ProcessAgent hijo PID6980 con tokens elevados en la sesión interactiva. El servicio seguía Running bajo LocalService PID25096. El Host React .102 en token medio confirmó que no había sesión activa. No había overrides RUNASADMIN en AppCompat para Desktop.

El código de install-focusguard.ps1 usa Start-FocusGuardInteractiveProcess y Shell.Application, con el comentario de que ShellExecute debe usar el token de Explorer. Sin embargo, la función sólo verifica que aparezca el ejecutable instalado; no verifica token, SID ni nivel de integridad. La política local ConsentPromptBehaviorAdmin=0 eleva automáticamente procesos administrativos nuevos, pero no se cambió durante esta prueba.

Con el servicio inactivo, se cerró el Desktop oculto y el Agent hijo; no se detuvo ni reinició el Service. Al abrir FocusGuard.Desktop.exe directamente desde el shell medio, Desktop PID2080 y ProcessAgent PID25352 quedaron ambos en token medio; el servicio conservó PID25096/LocalService. La UIA nativa pudo leer Classic.

## Hipótesis pendiente

La ruta Shell.Application llamada desde el instalador elevado no está garantizando que el cliente UI herede el token limitado del usuario en esta configuración UAC. La hipótesis aún requiere una reproducción controlada de esa misma llamada desde ambos niveles antes de cambiar el launcher.

## Siguiente paso

Comparar el token de un proceso inocuo lanzado mediante Shell.Application desde un proceso medio y desde uno elevado. Después corregir el launcher y su verificación para que Setup no reporte éxito mientras Desktop o ProcessAgent tengan token elevado. Añadir una regresión instalada y volver a probar con el Service inactivo; no cambiar la política UAC ni sus ACLs.

Evidencia del runtime: artifacts/fg305-output/fg291-classic-restart-elevation.json. La repetición Classic y la expiración natural están en artifacts/fg305-output/fg291-classic-repeat-0.1.102.json.

## Manifestas y límite de reproducción

La extracción de manifestas PE confirmó asInvoker en FocusGuard.Desktop.exe, FocusGuard.ProcessAgent.exe y FocusGuard.UI.exe; sólo FocusGuard Setup exige Administrator. No se encontraron capas RUNASADMIN en HKCU/HKLM. Una ejecución directa de Desktop desde el shell medio produjo Desktop/Agent medium; el arranque oculto posterior a Setup había producido ambos elevated. El servicio no cambió.

La hipótesis es que Start-FocusGuardInteractiveProcess (PowerShell elevado -> Shell.Application.ShellExecute) no garantizó la separación al token limitado en esta configuración UAC. La verificación de la función sólo espera el path y proceso; no consulta elevación.

Se intentó un probe controlado de Shell.Application desde un proceso elevado; el ejecutor lo rechazó con “blocked by policy” antes de iniciar el proceso. No se buscó una vía alternativa. No se modificó código ni política UAC. Root cause exacto y fix quedan pendientes; el runtime actual fue relanzado de forma segura como medium.

## Token-launch API research

A read-only token query of Setup PID21716 confirmed TokenElevationType=Full and a linked token with elevation=0, TokenElevationType=Limited, the same logged-on SID and SessionId 1 as the current medium shell. The token was inspected then closed; it was not duplicated and no child process was launched. Microsoft documents TokenLinkedToken as the handle returned by GetTokenInformation and CreateProcessWithTokenW as a candidate API; it requires SE_IMPERSONATE_NAME plus TOKEN_QUERY/TOKEN_DUPLICATE/TOKEN_ASSIGN_PRIMARY on the primary token and runs in the caller session ([TokenLinkedToken](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-token_linked_token), [CreateProcessWithTokenW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithtokenw)). A controlled elevated launch probe was rejected by the executor policy. The linked-token approach remains unimplemented and unverified.
