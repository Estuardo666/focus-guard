# CLOUD-1 — Checkpoint 4: cuenta nativa en Host/React

Fecha: 2026-10-12 (America/Guayaquil). Estado: **Fase 1 en curso**.

## Implementado

- El cliente nativo guarda issuer, client ID, subject, nonce original y tokens en un vault DPAPI CurrentUser. Al restaurar valida el archivo y el contexto OIDC, renueva el access token cerca de su expiración, persiste el refresh token rotado antes de confirmar la sesión y comprueba la sesión actual con `userinfo`. Un refresh anterior se rechaza; logout borra el vault local y revoca la sesión de Keycloak.
- Keycloak puede omitir el nonce en el ID token que emite al renovar. El validador sigue exigiendo el nonce esperado en el login inicial y, durante refresh, permite que el claim no aparezca; si se presenta, exige que coincida con el nonce original almacenado bajo DPAPI. En ambos casos comprueba firma, issuer, audiencia, expiración, subject y `azp`.
- El Host crea una cuenta separada del motor local. Las acciones del bridge se limitan a obtener estado, iniciar/cancelar login, logout y abrir el portal de seguridad. React recibe sólo nombre/correo/estado verificado; `deviceLinked` queda siempre `false`.
- El apartado Cuenta usa navegador externo para autenticación y ajustes de Keycloak. Indica explícitamente que autenticarse no vincula el dispositivo ni habilita órdenes remotas. Si la nube falla, las sesiones locales siguen disponibles.
- El build del instalador usa `-m:1` y `BuildInParallel=false` durante `dotnet publish`, para que la publicación autocontenida no agote memoria.

## Verificación

- `powershell -NoProfile -ExecutionPolicy Bypass -File cloud/deploy/Test-KeycloakNeon.ps1`: **59 checks nativos/API y 16 BFF** contra Keycloak/Neon reales; incluye restore tras un proceso nuevo, refresh rotation, rechazo del token anterior, logout, revocación y reinicio del contenedor. Exit code 0.
- `npm test` en `src/FocusGuard.UI`: **227/227**.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release`: **203/203**.
- `dotnet test tests/FocusGuard.Setup.Tests/FocusGuard.Setup.Tests.csproj -c Release`: **34/34**.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-installer.ps1`: PASS; `FocusGuard-0.1.149-Setup.exe --package-self-test`: exit 0.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -Version 0.1.149`: standalone Setup y ZIP regenerados tras el último cambio; payload contiene el Host, UI y `FocusGuard.Identity.dll`. El smoke de paquete y hashes de assets embebidos pasan. SHA-256 del Setup: `8F0BD2EDCC0876640EBDE5FB10CFEFA7DC428D816E16A0D9D99106D0E16FF278`. El instalador no se instaló.
- El primer publish paralelo agotó memoria; la compilación serial de `build-installer.ps1` completó todos los proyectos y generó el bundle. No se detuvieron procesos de otras aplicaciones.

## Archivos principales

`cloud/identity/Native/IdentitySession.cs`, `cloud/identity/Native/SignedIdentityTokenValidator.cs`, `src/FocusGuard.Host.Windows/Hosting/HostAccountService.cs`, `src/FocusGuard.Host.Windows/Bridge/`, `src/FocusGuard.UI/src/native/account.ts`, `src/FocusGuard.UI/src/AccountPanel.tsx`, recursos de idioma y estilos, tests de UI/Host y `scripts/build-installer.ps1`.

## Límites abiertos

El login distribuido apunta al dominio previsto pero aún no puede conectarse en vivo: Google Cloud no enruta `auth.`/`api.` hasta verificar propiedad de Search Console, y el SMTP de Keycloak no está configurado. `registrationAllowed` sigue desactivado; no se ha probado con cuentas reales correo de alta, verificación, reset o TOTP. El portal BFF también sigue sin variables de producción en Vercel. No se habilita vinculación ni control de dispositivos.

Una solicitud de autorización para agregar `transcitynorwalk@gmail.com` como propietario verificado del dominio sigue pendiente. La autorización se pidió porque concede a esa cuenta capacidad de administrar mapeos Cloud Run bajo `totemmassmedia.com`.

Estado posterior: los limites cloud anteriores estan resueltos en docs/progress/20261012-cloud1-public-acceptance.md. HTTPS real, SMTP, portal y pruebas de recuperacion/OTP/logout verificados; biblioteca nativa prueba HTTPS7 PASS. No queda pendiente conceder propiedad Search Console a Transcity.

