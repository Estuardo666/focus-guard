# FG-383 — plan: continuidad de sesión en Host .133

## Estado inicial y alcance
Servicio .133 Running como LocalService; Host `--pilot` del propietario a 1216×799, ServiceReady/ExtensionReady true, `SessionState=Inactive`, `Strict=false`. La prueba es un Timer normal de 1 min, perfil `2`, bloqueo OFF, Strict OFF. No se tocarán perfiles ni reglas.

## Ejecución prevista
1. Copiar `desktop-settings.json` y anotar SHA-256/LastWrite.
2. Preparar y revisar la sesión normal en React; confirmar duración y enforcement OFF en el resumen de revisión.
3. Leer Diagnostic status para capturar `SessionId`/`EndUtc` Active.
4. Cerrar solo la ventana Host mediante WM_CLOSE; no invocar Stop ni cambiar el servicio. Reabrir con `FocusGuard.UI.exe --pilot` como usuario.
5. Confirmar por Diagnostic y UIA el mismo `SessionId`/`EndUtc`, estado Active y canales; observar expiración natural.
6. Cerrar Host, restaurar el JSON original byte por byte y fecha/hora, reabrir Resumen y verificar la máquina final.

## Efectos y límites
La prueba crea una sola entrada de historial completada; se conservará para no borrar datos de usuario. El bloqueo está desactivado durante el minuto y Strict no se usa, por lo que no se cierran aplicaciones ni se necesita recuperación.