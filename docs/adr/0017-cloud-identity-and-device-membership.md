# ADR 0017 — Identidad, membresía y consentimiento de dispositivos

Fecha: 2026-10-11. Estado: decisión de diseño CLOUD-0, no desplegada.

## Decisión

Keycloak OIDC autoalojado para correo/usuario, contraseña, verificación, MFA y recovery de login. ASP.NET Core aplica permisos del producto. No desarrollar un almacén de contraseñas propio.

Nativos: navegador del sistema, Authorization Code + PKCE S256, state/nonce, redirects exactos y sin client secret público. React recibe solo estado sanitizado; tokens y claves bajo identidad nativa del usuario. Web/PWA: BFF con cookie Secure/HttpOnly/SameSite y CSRF. BFF puede custodiar token OIDC, no claves E2EE.

Cuenta = issuer/sub. Device = instalación vinculada con clave propia; login no demuestra pertenencia de instalación. API operativa exige token y prueba de posesión firmada; cookies BFF no sustituyen firma de dispositivo.

## Bootstrap sin depender de un dispositivo ya vinculado

Endpoints separados: bootstrap/challenge, bootstrap/accounts, bootstrap/pairings, bootstrap/pairings/{id} y device-sessions. Exigen identidad OIDC verificada y login reciente, pero nunca dan acceso a documentos ni comandos.

Primer dispositivo: transacción crea generationId y ancla recovery, dispositivo y primera membresía firmada; nonce de un uso evita dos primeros dispositivos concurrentes. El challenge vincula issuer/sub, deviceId y claves propuestas.

Otros dispositivos: solicitud pendiente con nonce de 32 bytes, TTL 300 s y máximo 5 intentos de código manual por solicitud. Login de ambos debe pertenecer a la misma cuenta. El aprobador firma nueva membresía y entrega claves cifradas para la huella exacta. Comparación de huella independiente en ambos; nunca mostrar QR con token/secreto.

Un solicitante pendiente consulta solo su propia solicitud para recoger sobres; no lista equipos ni documentos. Consume challenge/request una vez. Solo pasa active tras confirmar recepción y firma con su clave.

La lectura pendiente requiere prueba de la clave propuesta vinculada al challenge y requestId/timestamp (X-FG-Pending-Proof), además del login OIDC. La clave de verificación viene del registro de solicitud, no de un header reemplazable. No exigir device session antes de aprobar, ni confiar solo en un pairingId opaco como autorización.

## Membresía y recovery

Cada revisión firma baseRevision, proposedRevision, generación, miembros/roles/keys, epoch y hash anterior. CAS por cuenta. Miembro activo con manageDevices puede aprobar/revocar; el receptor verifica cadena y ancla.

Kit recovery contiene secretos distintos para descifrar claves y firmar transición de recuperación. Recovery requiere login reciente más prueba del kit, revoca sesiones operativas y provisiona nueva membresía. Sin kit, reset crea generación nueva, no sustituye ancla de equipos previos; volver a vincular y consentir localmente.

Revocar dispositivo deniega de inmediato lecturas/eventos/escrituras, cancela tickets no consumidos y cierra conexión. Rotación E2EE según ADR 0018. Dispositivo offline puede conservar contenido recibido y ejecución Strict finita; no prometer borrado remoto.

## Consentimiento local

Windows liga cuenta/generación/deviceId a SID y scope de logon observados por OS, no recibidos por JSON. Rol Sync nuevo en deployment manifest protegido, hash/trusted binary validado y allowlist de operaciones propia. Ni Desktop ni Diagnostic se usan como atajo.

Permisos normales y Strict distintos, iniciadores y perfiles permitidos, canales y duración máxima. Nuevos equipos no heredan iniciador Strict por defecto. Consentimiento local puede estrechar política cloud.

API protege objeto por cuenta y firma por instalación. Headers operativos incluyen deviceId, requestId único, timestamp y proof. Proof firma dominio, método, request-target exacto, SHA256 de body, SHA256 de access token, deviceId/generation y timestamp. Ventana inicial ±60 s, nonce de replay hasta 5 min; fuera de ella challenge nuevo. Reintentos no reutilizan requestId. No incluir secretos en URL/logs.

Strict requiere autorización de operación de un uso, TTL máximo 120 s, ligada al intentDigest y revision de destinos; backend valida auth_time ≤5 min y MFA si configurado. No confiar en booleanos del frontend.

## Fuentes y límites

[OAuth nativo RFC 8252](https://www.rfc-editor.org/rfc/rfc8252.html), [seguridad OAuth RFC 9700](https://www.rfc-editor.org/rfc/rfc9700.html), [Keycloak OIDC](https://www.keycloak.org/securing-apps/oidc-layers).

La firma de request es protocolo de producto basado en primitivas estándar, no un protocolo declarado auditado. Revisión de autenticación/replay e interoperabilidad obligatoria antes de distribución. Un verificador local Android para cadenas firmadas `FG-MEMBERSHIP-1` pasó un vector C# en AVD; aún no sustituye OIDC/API autenticados, vínculo del genesis a la cuenta, recovery ni pruebas de revocación en producción.

