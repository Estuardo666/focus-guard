# CLOUD-1 — Identidad y cuenta

Actualización: el estado más reciente de identidad, Host/React e instalador está en [checkpoint 4](20261012-cloud1-desktop-account.md); el BFF/ensayo cloud está en [checkpoint 3](20261012-cloud1-bff-oidc.md). Este documento conserva el primer plan y sus resultados históricos.

Estado: en curso. Autorización del propietario: iniciar Fase 1; Samsung disponible, ensayo físico pospuesto.

## Plan de implementación

Primer checkpoint: configuración reproducible Keycloak, clientes nativos PKCE y BFF separados, biblioteca nativa de identidad con estado sanitizado y custodia DPAPI por usuario, pruebas reales contra identidad local. Después: integración de Cuenta en React/Host y BFF, staging HTTPS/correo, pruebas completas y paquete Windows. No declarar Fase 1 completa antes de cumplir todos sus criterios.

La cuenta es opcional para el bloqueo local. Ningún cambio a autoridad del servicio, recuperación o sesiones estrictas. Contraseñas se introducen exclusivamente en identidad, nunca en FocusGuard. No usar cuentas ni claves personales en pruebas. El entorno local es desarrollo, no staging productivo; datos y credenciales desechables fuera de Git.

## Observaciones iniciales

Docker CLI presente, daemon detenido. JDK 21 portátil existente. Se validará Keycloak oficial portátil 26.8.0, SHA256 publicado por release GitHub `7ed1de3fda2598369262613bf682aab7e233d80a38c405e91588f7a7454370a1`, ligado exclusivamente a loopback. No instalar servicios, elevar ni cambiar configuración del sistema.

Fuentes: https://www.keycloak.org/server/configuration y https://www.keycloak.org/securing-apps/oidc-layers.

## Checkpoint 1 — Base de identidad nativa

Implementados en `cloud/identity/`: plantilla Keycloak sin cuentas/secretos, documentación de staging y límites, ejecutor local desechable, biblioteca Windows de identidad, runner de comprobaciones y lockfiles. La biblioteca queda fuera de `FocusGuard.slnx` y del paquete distribuido hasta completar integración y gates.

PKCE S256 con PAR cuando se anuncia, nonce aleatorio, state de un uso, vencimiento monotónico a 5 min y exclusión de login paralelo. Adapter de JWT usa Microsoft.IdentityModel.JsonWebTokens 8.23.0: valida firma RS256 con JWKS, emisor exacto, audiencia, sub, azp y nonce. Duende 7.1.0 no se deja con un validador sin firma. La respuesta permitida para React contiene solamente estado, nombre, email y verificación.

Callback real en Kestrel/IPv4 loopback por socket sin elevación ni URL ACL. Una única ruta, sin logging, headers no-store/CSP/no-referrer y respuesta sin código. Textos de callback con recursos inglés/español. El lanzador normal usa navegador del sistema; **no se ejecutó un recorrido visual con ese lanzador**. Los tests conducen identidad real con un agente HTTP y el callback real. El agente ajusta solamente sus cookies Secure para el fixture HTTP loopback; el transporte nativo no relaja HTTPS fuera de `LocalTest` explícito.

DPAPI CurrentUser, escritura reemplazada atómicamente, límites de tamaño, fallo ante manipulación y borrado automático del vault al logout. Logout local se despeja antes del cierre remoto; no afirmar revocación si la petición falla. Restauración/rotación/refresh todavía pendientes de siguiente checkpoint; este vault es una base probada, no persistencia de sesión integrada en producto.

### Validación

- `dotnet build cloud/identity/Tests/FocusGuard.Identity.Tests.csproj -c Release`: 0 avisos, 0 errores.
- `dotnet format` de ambos proyectos y comprobación posterior.
- `Test-LocalIdentity.ps1`: 41 comprobaciones pasando contra Keycloak 26.8.0 real y DPAPI Windows. Incluyen rechazo de password grant, PKCE obligatorio, código sin verifier, state alterado/replay, JWT con audiencia/emisor/nonce/firma incorrectos, DPAPI alterado, logout y refresh rechazado después, expiración/cancelación, socket nativo, ruta ajena 404 y liberación del puerto.
- `dotnet list ... package --vulnerable --include-transitive`: sin paquetes vulnerables reportados por el feed consultado; no equivale a auditoría exhaustiva.
- Helper local usa `dev-mem`, termina sus propios procesos, elimina import sintético y restaura variables del proceso. Puertos 18443/18444 sin listeners al terminar.
- `src`, `tests`, `extension`, `scripts` del producto: sin diff en este checkpoint; no regenerar installer porque ningún componente distribuido cambió.
- Ejecución final: evidencia sin secretos en `artifacts/cloud-phase1/identity-checks.txt`, 41 PASS; verificación de formato y `git diff --check` exit0. `IDENTITY_LISTENERS=0`, `TEMP_REALM_EXISTS=False`. `graphify update .` exit0, grafo actualizado a 19,078 nodos y 87,918 aristas.

### UX, seguridad y privacidad

La contraseña se introduce en identidad; no se añade formulario de password dentro de React. Ninguna cuenta de usuario real usada. No se activa bloqueo, Strict ni permisos de Samsung. ZDR parcial sigue vigente: identidad conserva los registros necesarios al desplegar; no anunciar retención cero de cuentas/sesiones.

El propietario preguntó por Vercel. Se verificó su soporte actual de contenedores y WebSockets; no se repetirá la limitación histórica de que nunca soporta WebSockets. Propuesta inicial: portal/PWA en Vercel, Keycloak/backend con alojamiento separado y persistencia. No hay proveedor contratado, servidor desplegado, dominio ni SMTP provisionados.

### Pendiente / próximo checkpoint

Fase 1 **en curso**: lifecycle de sesión (restaurar/refresh/revocación), BFF/cookies HttpOnly/Secure/CSRF, perfil y seguridad, Cuenta en Host/React con bridge sanitizado, SMTP/verificación/reset/MFA reales, staging HTTPS y distribución/instalador. No cerrar CLOUD-1 por estas pruebas de base. La prueba física de Samsung se mantiene pospuesta por decisión del propietario.
