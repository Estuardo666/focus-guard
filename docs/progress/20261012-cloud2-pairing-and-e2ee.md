# Fase 2 — pairing Windows y documentos E2EE

## Objetivo del checkpoint

Completar el flujo visible para solicitar y aprobar un segundo Windows, recuperar el estado después de una respuesta de red ambigua y probar los documentos cifrados contra Keycloak real del fixture, Neon y Windows DPAPI. Sin instalar el paquete ni desplegar cambios a Cloud Run.

## Cambios

- El panel de cuenta distingue el primer dispositivo de una instalación nueva en una cuenta que ya tiene miembros. El equipo nuevo solicita pairing, muestra la huella calculada desde sus propias claves y permite consultar la aprobación.
- El dispositivo aprobado puede actualizar su lista de solicitudes. El botón de aprobación abre un diálogo WinForms separado que muestra la huella, tipo/versión/ID del dispositivo y exige marcar que se comparó antes de firmar la membresía CAS.
- React recibe sólo `pairingId`, `deviceId`, plataforma/versión, huella y caducidad. El puente rechaza respuestas con propiedades extra o campos de pairing mal formados. No expone claves públicas sin necesidad ni ningún secreto, recovery kit o token.
- La nueva identidad se guarda primero con DPAPI y el ID de challenge —que también es el ID de pairing— se persiste antes de enviar el POST. Si no hubo commit, un 404 permite iniciar un challenge nuevo con la misma identidad; si el servidor sí recibió la solicitud, la app recupera ese estado en lugar de crear claves huérfanas.
- El solicitante compara la huella devuelta con la calculada localmente antes de aceptar la membresía. La confirmación registra una sesión aleatoria generada en el equipo; el servidor guarda sólo su hash. El aprobador y el solicitante validan las claves y sobres firmados.
- Se subió la versión de ensayo a `0.1.151`. El arnés integrado mueve la comprobación de replay al final de la ventana de rate limit real y limpia también `membership_history`, `device_sessions` y `request_proofs` antes de borrar su cuenta sintética.
- El smoke de instalador ahora mantiene vivo el proceso oculto más tiempo y vuelve a intentar la consulta CIM; así verifica su SID sin una carrera por el proceso de tres segundos.

## Verificación

- `npm run check`: pasa. `npm test -- --run`: 233/233.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release -m:1 --no-restore`: 208/208.
- `dotnet test cloud/devices/Tests/FocusGuard.Devices.Tests.csproj -c Release -m:1`: 14/14.
- `npm test -- --run` en `prototypes/cloud-phase0`: 36/36; vectores de documentos y sobres intercambian ciphertext/firmas entre C# y Node.
- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly`: 103 comprobaciones pasan con OIDC/JWKS, Neon TLS, bootstrap, segundo dispositivo, sesiones por hash, documentos CAS cifrados, aislamiento entre dos cuentas, revocación, historial de membresía, replay y DPAPI. Además, el realm sobrevive un reinicio de su contenedor. Todos los datos sintéticos se limpian; el contenedor local se detuvo al finalizar.
- `scripts/build-installer.ps1 -Version 0.1.151`: build y publicación win-x64 completados; 19 recursos del Host UI incluidos.
- `scripts/test-installer.ps1`: pasa; `FocusGuard-0.1.151-Setup.exe --package-self-test`: exit 0; `scripts/test-browser-extension-package.ps1 -Version 0.1.151`: pasa Chrome/Edge.
- Setup SHA-256: `FD8940AC387652FFEDA8C29F01B2C6D701AFAAAE2014021D541406FB95BDAB43`.
- ZIP SHA-256: `05DDFC97EBCB1B5C833ABE057F5DFCD3B6D96E189DF6DA10203E3B05474F90C4`.
- API de staging TNC desplegada como `tnc-api-staging-00014-bn8`, digest `sha256:fb2d077dd18db1889b88f8ef82bac9a5eeddf72cf27b7da15f79b8dae8877fe6`. `/health/live` y `/health/ready` devolvieron 200; `Test-TncStaging.ps1`: 55 checks. El primer smoke se revirtió porque usaba `-SkipHttpErrorCheck`, inexistente en Windows PowerShell 5.1; se corrigió el manejo de códigos HTTP y el despliegue posterior pasó.
- `git diff --check`: sin errores; sólo avisos de normalización LF/CRLF en archivos preexistentes.

## Límites y siguiente checkpoint

El ensayo no vinculó la cuenta real ni instaló FocusGuard; la instalación actual no cambió. Las comprobaciones de pairing/documento usan dos identidades de ensayo en un mismo host físico; no equivalen a dos PC reales. La API sí se desplegó a staging, pero el portal no cambió. La comparación de huella es manual y todavía no hay QR en la UI. Restan transferencia inicial de ajustes seleccionados con consentimiento, prueba física en el segundo Windows, reset real de contraseña sin descifrado y vault/interoperabilidad Android. Por tanto, Fase 2 sigue abierta.

## Continuación — 2026-10-13

### Cambios posteriores al checkpoint

- El código de solicitud efímero ahora aparece junto a la huella en Windows y Android; el equipo que aprueba también lo muestra en su confirmación nativa. Se presenta como identificador, nunca como autorización. No hay lector/cámara QR en esta versión.
- Se amplió el arnés local para reconciliar en su realm sintético el cliente Android público, redirect exacto, PKCE S256 y audiencia API, incluso cuando el realm ya existía en Neon.
- La aceptación sintética ahora realiza un cambio real de contraseña en Keycloak y verifica que el nuevo secreto por sí solo no descifra el documento E2EE almacenado. La cuenta y la contraseña son sintéticas; no se tocaron las credenciales personales.

### Verificación actual

- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly`: **122 comprobaciones** aprobadas con Keycloak, Neon TLS, API y DPAPI reales. Verifica pairing/replay, membership, documentos CAS cifrados, segunda identidad, aislamiento de otra cuenta, revocación, renovación, cambio de contraseña y limpieza de datos. El realm y sus claves sobrevivieron el reinicio del contenedor.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release -m:1 --no-restore`: **221/221**.
- `dotnet test cloud/devices/Tests/FocusGuard.Devices.Tests.csproj -c Release -m:1`: **19/19**.
- `npm run check` y `npm test -- --run` en `src/FocusGuard.UI`: TypeScript correcto y **236/236**.
- `npm test -- --run` en `prototypes/cloud-phase0`: **38/38**, incluidos vectores C#/Node.
- Android SDK 36: `lintDebug connectedDebugAndroidTest` exitoso, **5/5** en `FocusGuard_Phase0_Android15` (AVD API 35 x86_64). El APK no inicia bloqueo ni se probó en hardware Samsung.
- El instalador independiente **0.1.158** pasó `scripts/test-installer.ps1`, `--package-self-test` y `scripts/test-browser-extension-package.ps1 -Version 0.1.158`. Setup SHA-256 `31BAA0B22EDD303CA43394B38B75B800FFFCC7EA38D90FA729EE63F93A571F62`; ZIP SHA-256 `271E46855F08FEA05524CC945B5B3FF9C7232B864F70FE2518B50883E4628921`.
- APK de ensayo copiado a `artifacts/cloud-phase0/android-pairing/focusguard-android-phase2-alpha.apk`, SHA-256 `ED4C70072B51BF2848E4E98C085251F09471D1FC6A00509F9E84E8D7D47F6DCC`.
- `git diff --check`: sin errores; sólo avisos de conversión LF/CRLF en archivos preexistentes.

### Pendiente para cerrar la Fase 2

La aceptación final pide dos PC Windows físicos. La integración actual valida dos dispositivos sintéticos desde el mismo host, y no se instaló el paquete nuevo en la PC del hermano. Debe comprobarse allí que el segundo equipo recibe membresía y descifra el documento propio. El Samsung S24 tampoco estuvo conectado; el AVD prueba el vault, el contrato OIDC y la criptografía Android, pero no reemplaza la prueba ARM64/OneUI. La UI de vínculo ya ofrece un código manual de solicitud en vez de QR; la API demuestra consumo único y rechazo de replay. Ninguna sesión de bloqueo ni configuración del bloqueo local se modificó.

### Limpieza de artefactos antiguos

Se verificaron como obsoletos los paquetes .143, .144 y .138, además del backup de emulador 37.2.12 (aprox. 5.61 GiB junto con el ZIP .138). La revisión automática de comandos rechazó tanto la eliminación recursiva de las carpetas como la operación de limpieza subsiguiente; no se borró ninguno de esos artefactos. Se conservaron los instaladores .156/.157/.158 y el emulador 37.3.3 validado.
