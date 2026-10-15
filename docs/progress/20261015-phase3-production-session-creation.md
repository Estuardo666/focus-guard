# Fase 3 — creación productiva de la sesión al aceptar el opt-in

## Resultado

Se añadió una prueba de integración que entra por `account.settings.sync.enable`, atraviesa `NativeBridgeDispatcher` y `HostAccountService`, carga el vault DPAPI de un dispositivo ya vinculado, compone la sesión con `HostSettingsSyncSession.Create` y publica el baseline E2EE con consentimiento persistente. La teoría cubre las dos ramas de `EnsureDeviceSessionAsync`: validar una sesión vigente y recuperar una sesión rechazada con renovación firmada, seguida por una segunda consulta de membresía.

La prueba no inyecta una sesión de sincronización precompuesta. El handler local valida el bearer, el identificador de sesión del dispositivo, la prueba de renovación con su dominio de firma y las pruebas firmadas de las demás peticiones; acepta el fallback `404` del cursor y almacena solo el sobre cifrado. En el caso de renovación comprueba que el secreto nuevo quedó persistido en el vault DPAPI. También confirma que no modifica el vault OIDC y conserva los ajustes locales fuera de la allowlist.

Además, el harness local crea una cuenta con Keycloak real, registra un dispositivo en el API/PostgreSQL y después activa sync por el bridge/`HostAccountService` real. El Host usa el access token real de Keycloak; su handler de prueba reenvía peticiones a un `WebApplicationFactory` aislado que comparte la misma base PostgreSQL y valida bearer, firma de dispositivo, membresía y CAS. El test comprueba el baseline E2EE en la tabla del API y que los ajustes ajenos a la allowlist sigan locales. La identidad de host del fixture usa un issuer HTTPS de prueba para aislar el scope DPAPI, y su estado en memoria se prepara en el harness; el login OIDC/DPAPI real se prueba aparte en la misma suite. El `TestServer` no realiza handshake TLS; Kestrel con certificado fijado mantiene esa cobertura independiente.

El handler alternativo de `IdentitySession` permanece interno y rechaza configuración normal. La DLL de producción no expone un método para sembrar tokens sintéticos; el harness usa reflexión sobre su instancia de prueba.

## Verificación

- Prueba focalizada `AcceptedOptInCreatesProductionSessionFromLinkedVaultAndValidatesMembership` — **2/2** casos.
- Suite `FocusGuard.Host.Windows.Tests` — **244/244**.
- `dotnet format whitespace FocusGuard.slnx --verify-no-changes --no-restore --include ...` para los cinco archivos de código cambiados — PASS.
- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly -LocalPostgres` — **152 checks** PASS contra Keycloak local real, API respaldada por PostgreSQL y DPAPI Windows. Incluye las 20 comprobaciones de sync existentes y 7 nuevas de opt-in Host con bearer real, membresía, baseline cifrado y preservación de los campos locales. El realm conservó la firma tras reiniciar el contenedor.
- Setup `.174` y ZIP de menú generados para la aceptación física. UI package version también se alinea a `.174`; `scripts/test-installer.ps1`, guard de cliente de producción y `--package-self-test` pasan. Setup SHA-256 `931F03298C162F73146919E027751043063DC1EC734391DA9507E854CEC2DBFA`; ZIP SHA-256 `61E20EF21F98F205EC82A23A67C8A5EAA03FEBB6C5BCEEE8C6FA261552BAAFEE`.
- Los dos contenedores efímeros y el archivo temporal del realm quedaron eliminados. El paquete no se instaló, no se contactó staging y no se alteró una cuenta real.

## Límite

La prueba local de la clase Host cubre la fábrica productiva con tokens sintéticos. El harness adicional conecta `HostAccountService` al API/PostgreSQL con un JWT de Keycloak real, pero no restaura una sesión OIDC desde el host ni realiza TLS de red en ese camino. Tampoco sustituye el servicio desplegado ni la convergencia entre dos PCs físicas. La aceptación de dos PCs y la validación posterior contra staging siguen siendo gates separados.
