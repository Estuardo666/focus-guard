# Fase 3 — preparación de la aceptación física

Se preparó un runbook reproducible para el gate de dos PCs Windows pendiente de Fase 3: instalar la versión candidata idéntica `.174`, confirmar consentimiento independiente, observar convergencia automática, cerrar/reabrir, ejercitar cambios offline y conflicto por campo, restablecer los ajustes compartidos y resolver un conflicto de tombstone. `.174` conserva el wake-up de red consentido, el cursor account-wide y evita descargar el documento si el cursor no cambió; las señales no deben saltar un `Retry-After` persistido.

El procedimiento distingue la sincronización automática de la transferencia manual de un solo uso, conserva los vínculos existentes, limita los cambios a theme/language/default duration y pide comprobar que reglas, perfiles, permisos, historial, enforcement y sesiones activas permanezcan locales. También indica cómo registrar resultados sin credenciales ni material de recuperación y cómo detenerse si aparece `Attention`.

Los nombres de navegación y activación, además de los mensajes de sync pendiente, conflicto, reset y atención, incluyen los textos en inglés y español cotejados con `AccountPanel` y sus archivos de locale. La guía también ofrece los dos comandos PowerShell para comparar Setup y ZIP con los hashes `.174` en cada PC y una hoja para registrar los resultados por equipo y checkpoint.

**Verificación actual:** documentación cotejada con la UI/bridge/.174 y criterios de `docs/PLAN-CLOUD-MULTIDISPOSITIVO.md` sección 9 y Fase 3. Setup SHA-256 `931F03298C162F73146919E027751043063DC1EC734391DA9507E854CEC2DBFA`; ZIP SHA-256 `61E20EF21F98F205EC82A23A67C8A5EAA03FEBB6C5BCEEE8C6FA261552BAAFEE`. El build, guard de producción, `scripts/test-installer.ps1` y `--package-self-test` pasan. No se ejecutó en las PCs, no se instaló el paquete, no se cambió ninguna cuenta y no se llamó a staging.

**Pendiente:** realizar el runbook en ambas PCs físicamente y registrar valores/estados observados. Ver [runbook completo](../cloud-phase0/PHASE3-PHYSICAL-ACCEPTANCE.md).
