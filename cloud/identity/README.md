# Identidad CLOUD-1

Configuración de Keycloak 26.8.0. No es un despliegue ni está integrada todavía en el instalador Windows. `realm-template.json` no contiene usuarios, contraseñas ni secretos de cliente. Los hosts de producción/staging elegidos son `focusguard.totemmassmedia.com` para portal/BFF y hosts separados `auth.focusguard.totemmassmedia.com` y `api.focusguard.totemmassmedia.com`; configurar SMTP mediante Secret Manager y Admin REST antes de registrar usuarios. No usar `start-dev` en staging.

Clientes separados: nativo público, code + PKCE S256; BFF confidencial y PKCE. Sin password grant, implicit, service accounts ni orígenes wildcard. El callback HTTP está limitado a loopback nativo RFC8252; no autoriza HTTP del proveedor. Identidad registra/verifica correo y gestiona contraseña/reset/OTP mediante sus pantallas; no hay formulario de contraseña embebido en FocusGuard. MFA disponible por Account Console, no obligatorio universalmente en este checkpoint. MFA/reautenticación Strict se completará antes de esa fase.

El ejecutor local genera **otro realm desechable**, sin verificación de correo para probar sin SMTP, con una cuenta sintética y secretos aleatorios en `artifacts/`. Esa excepción no modifica la plantilla de staging. Eventos deshabilitados no equivalen a retención cero: Keycloak conserva cuentas, credenciales derivadas y sesiones necesarias. No recopilar navegación, perfiles ni claves E2EE.

Vercel: candidato para el portal/PWA. Soporta contenedores y WebSockets actualmente, pero no se ha validado aquí la operación de Keycloak con persistencia externa. La arquitectura inicial conserva identidad y backend en alojamiento separado; no hay despliegue automático ni una decisión de proveedor cerrada.

Fuentes consultadas 2026-10-11:
- https://www.keycloak.org/server/configuration
- https://www.keycloak.org/securing-apps/oidc-layers
- https://docs.duendesoftware.com/identitymodel-oidcclient/manual/
- https://vercel.com/kb/docker

Duende.IdentityModel.OidcClient 7.1.0 es Apache-2.0 (cliente FOSS; no es Duende IdentityServer ni su BFF comercial). Keycloak Apache-2.0; dependencia DPAPI Microsoft MIT. Lockfiles versionan la restauración exacta.

## Ejecutar la validación local

En Windows sin elevación, con .NET 10 y JDK 21:

```powershell
./cloud/identity/Test-LocalIdentity.ps1 -KeycloakHome 'RUTA/keycloak-26.8.0' -JavaHome 'RUTA/jdk21'
```

El script liga identidad a `127.0.0.1:18443`, usa base `dev-mem`, importa solamente el realm sintético, ejecuta comprobaciones y termina su propio helper. Reserva el callback `127.0.0.1:18444` solo mientras se necesita. Si el puerto está ocupado aborta; no termina procesos ajenos. Elimina el import temporal y la contraseña del entorno. La descarga oficial validada queda en artifacts.

Native contiene `IdentitySession`, `SignedIdentityTokenValidator` (Microsoft.IdentityModel.JsonWebTokens 8.23.0 MIT), `SystemBrowserLogin` y `UserTokenVault`. Kestrel abre un socket de usuario, sin HTTP.sys/URL ACL. El lanzador normal abre navegador externo; los ensayos usan un agente HTTP contra Keycloak real y socket real, **no acreditan un recorrido visual en navegador instalado**. Solo ese agente adapta cookies Secure en HTTP loopback; el cliente de identidad conserva HTTPS fuera del modo local explícito. PAR se usa cuando el proveedor lo anuncia, además de PKCE.

Hay verificación criptográfica de JWT RS256 contra JWKS, emisor, audiencia, sub, azp y nonce; state/callback se consumen una vez, vencen con tiempo monotónico y se serializan los intentos. DPAPI CurrentUser protege el archivo, rechaza manipulación y logout elimina el vault incluso antes de la petición de cierre remoto. Si falla el cierre remoto, el estado local se desconecta pero **no se afirma revocación confirmada en servidor**. No se registran URLs de callback ni tokens en el listener.

Pendiente antes de integrarlo en producto: restaurar y renovar sesión desde vault con rotación/fallos/revocación, almacenamiento bajo carpeta y ACL del usuario, perfil/seguridad, BFF/cookie/CSRF, pantallas React y bridge sanitizado, SMTP/verificación/reset/MFA end-to-end, staging HTTPS y versión del instalador. Este checkpoint no publica componentes ni modifica el bloqueo local.
