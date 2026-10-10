# FG-395 — continuidad Host/Service en .135

## Resultado (2026-10-09)

Desde el Host React instalado preparé y confirmé un Timer normal de 1 minuto en perfil `2`, Strict OFF y enforcement OFF. El servicio reportó `SessionId=a94f44fa-3e81-493e-af6f-35cd168b22fd` y `EndUtc=2026-10-10T03:57:38.1183345Z`.

Envié WM_CLOSE a la ventana (Host PID 12928); el proceso salió. `FocusGuard` siguió Running como `NT AUTHORITY\LocalService` PID 35760, con la misma sesión Active/SessionId/EndUtc y 37 segundos restantes. Reabrí `FocusGuard.UI.exe --pilot` como Stuart; el nuevo PID 35304 era owner en integridad Medium (RID 8192, no elevado). El Host reanudó la misma sesión con 28 segundos restantes; UIA mostró «Sesión en curso», Servicio Conectado, Extensión Disponible y «No solicitado». La sesión expiró al minuto sin detenerla; DrillClient reportó Expired/0s y el Host mostró «Sesión completada» y «Sin sesión activa».

No lancé la aplicación de prueba, no abrí sitios ni edité perfiles/reglas. La base de perfiles conservó 61,440 bytes y su LastWriteTime 22:48:49 (la escritura corresponde al add/remove de FG392); `desktop-settings.json` conservó el SHA original `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55`.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg395-installed-host-service-continuity-135.json`.
