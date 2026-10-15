# Fase 2 — verificación final del workspace, 14 de octubre de 2026

## Resultado

La aceptación principal física sigue cerrada según los reportes directos del propietario y las capturas registradas en `docs/cloud-phase0/PHASE2-PHYSICAL-ACCEPTANCE.md`: propietario→hermano importó tema, idioma y duración; sesión de cuenta y ajustes persistieron después de reiniciar y reabrir; reglas, perfiles y permisos permanecieron en el PC del hermano. Estos últimos son datos locales y no se sincronizan entre PCs.

## Verificación repetida en el workspace actual

- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --nologo -m:1`: **231/231** aprobados. El build de dependencias también ejecutó el check/build de producción React y verificó los assets locales/CSP.
- `dotnet test cloud/devices/Tests/FocusGuard.Devices.Tests.csproj -c Release --no-restore --nologo -m:1`: **19/19** aprobados.
- `npm test -- --run` en `prototypes/cloud-phase0`: **38/38** aprobados, incluidos vectores de interoperabilidad criptográfica C#/Node.
- `dotnet build cloud/identity/Tests/FocusGuard.Identity.Tests.csproj -c Release --no-restore --nologo`: compilación de Identity/API y arnés integrado exitosa, **0 warnings, 0 errors**. No ejecuta el arnés de Keycloak/Neon.
- `cloud/identity/Test-LocalIdentity.ps1`: después de corregir el fixture para aplicar `Native-SessionPolicy.ps1`, pasó **46/46** comprobaciones con Keycloak local y DPAPI. Ver [investigación de causa raíz](20261014-local-identity-fixture-fix.md). Esta ejecución no configuró `FG_API_TEST_CONNECTION`, por lo que no cubre API/Neon.
- Android en el AVD portátil `FocusGuard_Phase0_Android15` (Android 15/API 35, x86_64), con SDK 36 y JDK 17: `gradlew.bat lintDebug connectedDebugAndroidTest` pasó; **8/8** pruebas instrumentadas y lint exitoso. APK debug 0.2.0-alpha.3/versionCode 3, SHA-256 `7561FA6BA14AA43B58C8C02989E0D369437B01BF57F271196BC2A8DB157A6D89`. La compilación emitió advertencia de SDK XML v4 frente a parser que entiende hasta v3; no impidió las pruebas. Esto no es una nueva aceptación en Samsung/ARM64/One UI.
- Intento inicial de repetir `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly`: no ejecutó el fixture porque Docker no conectó a `dockerDesktopLinuxEngine`; iniciar el servicio desde este proceso falló por permisos. No se intentó elevar ni rodear la restricción. El propietario informó después que Docker volvió a estar disponible.
- Repetición integrada con Docker disponible: `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly` pasó **125 checks** con Keycloak 26.8, Neon TLS, API y DPAPI reales. Incluyó segundo dispositivo, documento propio descifrado por el receptor, aislamiento de otra cuenta, replay, revocación/rotación, logout, y cambio de contraseña sintética que no descifra el documento. El realm sobrevivió al reinicio del contenedor. El contenedor terminó detenido, el JSON temporal de importación se retiró y el puerto 18443 quedó libre. Una ejecución previa en este mismo ciclo reportó `UserInfo invalid_token`; la repetición posterior pasó y no se estableció la causa de ese fallo no reproducido. Todo se limitó al realm/cuentas sintéticas; no se desplegó API.
- Repetición final tras retirar la instrumentación diagnóstica temporal de `IdentitySession.cs`: **125/125** checks de nuevo; compilación final Identity/API **0 warnings, 0 errors**. Esto valida el resultado con el código de producto restaurado. El contenedor volvió a quedar detenido.
- La aceptación física Samsung sigue respaldada por las fotos/reportes previos del propietario; la ejecución de hoy solo repitió el APK de staging en el AVD. El AVD de ensayo se apagó después de verificarlo, sin borrar sus datos.

## Inventario de espacio

El árbol .164 bajo `artifacts/installer` está intacto: footprint completo de **1.878.703.503 bytes** (1.653 archivos incluyendo árbol, más Setup y ZIP externos). Los cuatro objetivos previamente seleccionados —Setup/ZIP externos, Setup interno y `publish/`— suman **1.877.969.140 bytes**. Su eliminación fue rechazada por política antes de ejecutar; no se intentó una ruta alternativa. El inventario actualizado y hashes están en `docs/progress/20261014-installer-artifact-inventory.md`.

Revalidación posterior a las pruebas Android (estado a esa hora): espacio libre C: **15,04 GiB**, D: **105,37 GiB**, E: **78,23 GiB**. C: tenía 17,04 GiB antes de iniciar el AVD; durante las pruebas bajó alrededor de 2 GiB. Inventario de solo lectura: copia AVD de usuario 4.057.171.763 bytes y caché Gradle 1.243.481.882 bytes, ambos con escrituras recientes; sin medición previa por carpeta no atribuyo toda la diferencia a uno de ellos. No borré ni moví esos datos.

Seguimiento posterior a los enlaces proporcionados: la carpeta y los dos instaladores `.164` ya no aparecen bajo `artifacts/installer`; `.165` y `.166` sí permanecen. No se ejecutó desde el agente un nuevo borrado tras el rechazo de política. D: pasó de 113.135.140.864 a 115.014.135.808 bytes libres (+1.878.994.944 bytes); espacio actual C: 15.411.699.712 bytes, D: 115.014.135.808 bytes y E: 83.995.131.904 bytes. La retirada de `.164` coincide temporalmente con el aumento de D:, sin atribuir exactamente el cambio a una causa única.

## Limitaciones y siguiente paso

Fase 2 principal está aceptada. Quedan opcionales los smokes de extensión, cancelación, importación parcial y Simple Timer descritos en la guía. La limpieza real de .164 permanece bloqueada por la política del entorno; no se debe cambiar de herramienta ni ubicación para eludir el rechazo. El trabajo de producto posterior es la integración de Fase 3, no sincronizar reglas, perfiles o permisos, que siguen siendo locales.
