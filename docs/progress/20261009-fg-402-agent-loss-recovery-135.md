# FG-402 — recuperación de ProcessAgent en .135

## Resultado (2026-10-09)

Con Host .135 abierto, inicié una sesión normal de 3 min con el perfil `gpt test`, enforcement ON y Strict OFF. El perfil solo bloquea `FocusGuard.TestTarget.exe` y permite `focusguard.invalid` (0 sitios bloqueados). El Host mostró Servicio Conectado, Extensión Disponible y Protección «Confirmada»; Actividad guardó 1 intento bloqueado.

Terminé una vez el ProcessAgent PID 6468 mientras la sesión estaba Active. `enforcementConnected` bajó a false; el servicio mantuvo Active, el mismo SessionId y el mismo EndUtc. El Host supervisor reinició Agent como PID 28088 (inicio 23:17:09); a las 23:17:15 el servicio volvió a reportar `enforcementConnected=true`, con 139s restantes. La UI volvió a mostrar «Confirmada». El TestTarget ya había sido bloqueado y no corría. La sesión expiró naturalmente como Expired, sin Stop manual.

El servicio siguió Running como LocalService PID35760. No se cambiaron reglas ni perfiles; la DB conserva 5 perfiles/5 reglas app/30 sitios. Restauré `desktop-settings.json` al SHA `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55` y LastWrite 13:19.

Esto prueba pérdida y recuperación del proceso Agent con enforcement activo en .135; no reemplaza reboot/rollback/Edge ni el resto de la matriz de roles.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg402-installed-agent-loss-recovery-135.json`, `fg402-testtarget-launch.json` y `fg402-testtarget-stdout.txt`.

## Captura visual complementaria (2026-10-09)

`D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg402-agent-recovery-summary-screen.png` muestra en Actividad la sesión `gpt test` de tres minutos como completada y el botón «Ver resumen» enfocado. No muestra el detalle del resumen ni el número de intentos; por ello, esta captura solo confirma la entrada de sesión completada y no se usa como prueba del intento bloqueado. El dato de 1 intento bloqueado permanece respaldado por la evidencia de ejecución/UIA registrada arriba.
