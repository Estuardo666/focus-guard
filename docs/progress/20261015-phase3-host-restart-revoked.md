# Fase 3: reinicio con consentimiento revocado

Plan: ampliar la prueba productiva de reinicio para desactivar sync mediante la acción normal del Host antes de cerrarlo. Crear otro Host con el mismo vault y preferencias, refrescar el vínculo y verificar que no crea sesión, no inicia lecturas de documentos, no publica otro baseline y no pide consentimiento. Comparar con el caso de consentimiento activo.

Seguridad y UX: la revocación debe sobrevivir al cierre sin impedir la verificación del vínculo. No instalar ni desplegar; los vaults y las solicitudes se mantienen aislados en el fixture local. Recuperación: preferencias y documento compartido deben conservarse.

Resultado: los cuatro casos (sesión de dispositivo renovada/vigente y consentimiento activo/revocado) pasan. Al revocar, el Host nuevo verifica el vínculo pero deja sync Disabled, sin sesión/runner y sin lecturas o publicaciones de documentos. Conserva las preferencias. El primer intento del fixture coincidía con la comprobación de dispositivo iniciada por Get; ahora espera esa operación y exige que DisableSettingsSync acepte la acción antes de cerrar.

Validación: `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj --no-restore` PASS 246/246; build y comprobación de assets UI incluidos. Formato whitespace focalizado PASS. Solo cambian pruebas y documentación, por lo que el paquete `.174` validado en el checkpoint anterior sigue vigente. Siguiente paso: aceptación física en dos Windows; no se realizó instalación ni staging.
