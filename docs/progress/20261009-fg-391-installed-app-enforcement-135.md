# FG-391 — bloqueo real de aplicación en Host .135

## Resultado (2026-10-09)

Usé el perfil existente `gpt test`, cuyo único bloqueo es `FocusGuard.TestTarget.exe`; su única regla web permite `focusguard.invalid` (0 sitios bloqueados). Desde el Host React instalado preparé y confirmé una sesión normal de 1 minuto, enforcement ON y Strict OFF. El servicio aceptó la sesión y la UI mostró «Confirmada». El PID 34780, iniciado 16 segundos antes con `--lifetime 120`, desapareció aproximadamente 16 segundos después de activar el bloqueo; por tanto no expiró por su propio límite. La sesión terminó naturalmente al minuto. Actividad registró `SessionId=38733498-178d-46d7-ad71-e3f0d3d17cce`, `Sesión completada`, 1 min y **1 intento bloqueado**. El Host quedó Inactive; el proceso de prueba no sigue abierto.

No inicié reglas de sitios ni abrí un navegador. La base de perfiles conservó 61,440 bytes y su LastWriteTime 22:18:14. La sesión escribió temporalmente la última preferencia usada; la restauré byte por byte al SHA previo `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55` y LastWrite 13:19. El Host quedó en Actividad con el resumen para esta sesión, sin otra sesión activa.

Evidencia estructurada y consola capturada: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg391-installed-app-enforcement-135.json`, `fg391-testtarget-launch.json` y `fg391-testtarget-stdout.txt`.

Esto valida bloqueo real de aplicación en .135. No valida bloqueo web, Edge ni el resto de las compuertas de Fase 7/9.
