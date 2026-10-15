# Fase 1: identidad y cuenta por HTTPS

## Alcance y evidencia

Se conserva Google Cloud bajo `transcitynorwalk@gmail.com`, proyecto `tnc-staging-2026` y recursos `tnc-*`. Neon mantiene bases/roles separados; portal/BFF en Vercel; correo transaccional en Resend. El proyecto GCP anterior permanece como respaldo. La Fase 1 no concede membresia de dispositivos ni control remoto.

| Requisito del plan | Evidencia de aceptacion |
| --- | --- |
| OIDC nativo y BFF separados | Clientes `focusguard-native` publico y `focusguard-bff` confidencial; code/PKCE, state/nonce y firmas. Discovery publico HTTPS 200. Imagenes fijadas por digest. |
| Login, registro, verificacion y reset | Registro real autorizado de estuarlito, correo recibido/verificado y password establecido por el usuario en Keycloak. Portal muestra cuenta verificada. Recuperacion enviada desde Forgot Password; usuario completo password nueva y nuevo login. |
| Perfil y seguridad | Consola account muestra perfil, password, OTP `PC W10`, aplicaciones BFF/account-console y sesion vigente. |
| Vault nativo y estado sanitizado | LiveProbe contra HTTPS: 7 PASS, PKCE/callback/firma, correo verificado, persistencia DPAPI, restauracion/refresh, revocacion y borrado de vault. Suite sintética adicional valida expiracion, replay y tampering. React recibe estado de cuenta, no tokens. |
| Rate limits, enumeracion y MFA | Realm brute-force=true/failureFactor5. API limiter 30/min por sujeto. Recuperacion de direccion inexistente devuelve HTTP200 y el mismo mensaje generico que la direccion real. Usuario configura OTP, conserva PC W10 tras reset y confirma nuevo login. Prueba real de limiter y no-store: PASS en suite nativa/API de 61 checks. |
| Logout y revocacion | Logout portal real: desconectado; nuevo login solicita autenticacion. Suite BFF 16 PASS incluye revocacion real y rechazo cookie anterior. LiveProbe HTTPS confirma logout remoto y vault borrado. |

## Cambios y problemas corregidos

- Certificados auth/API Ready y emitidos; DNS CNAME TTL300 en cPanel a Google. Se uso la propiedad de dominio ya verificada de estuarlito, sin agregar Transcity como propietario.
- API apuntaba a raiz Keycloak; corregida a issuer exacto `/realms/focusguard` en revision `tnc-api-staging-00002-tsb`.
- Vercel guardaba newline por `WriteLine`; script usa `Write` y variables sensibles/config recargadas. Deployment `2iYrRWjqWvjHuXrDruo9kWzSYxhs`; portal me200/login303.
- Plantilla de required actions omitia UPDATE_PASSWORD. Registrada/enabled/defaultAction=true y agregada a plantilla. Registro cerrado durante reparacion y reabierto con `Configure-KeycloakStaging.ps1 -EnableRegistration`, tras checks HTTPS identity/API.
- Reset Password incluia Reset OTP. Deshabilitada rama condicional OTP: email de recuperacion cambia password, no recupera segundo factor. MFA perdido necesita un flujo independiente; no se simula como terminado en Fase 1.
- Docker bind E/exFAT no encontraba import; OOMKilled=false. Fixture movido a NTFS, ejecutado con 2GiB/1CPU. Los ficheros de credenciales temporales se borran al finalizar. Primera ronda BFF fallo HTTP400; repeticion dedicada paso 16/16 y reinicio. No se ignora el fallo: diagnostico sanitizado añadido y registrado.

## Validacion distribuida previa

UI 227, Host 203 y Setup 34 tests; `test-installer.ps1` y self-test standalone PASS en checkpoint de cuenta. Setup 0.1.149 SHA256 revalidado: `8F0BD2EDCC0876640EBDE5FB10CFEFA7DC428D816E16A0D9D99106D0E16FF278`. Este checkpoint cambia configuracion/scripts de cloud y utilidades de aceptacion, no binarios Host/UI. No se instala automaticamente el paquete.

## Seguridad, privacidad y recuperacion

No hay secretos en respuestas de cuenta/browser ni en los mensajes de prueba. Cookies opacas HttpOnly/Secure/SameSite, CSRF logout y sesiones BFF AES-GCM en Neon. Usuarios introdujeron password/OTP directamente; no se solicitaron por chat. Native acepta callback solo IPv4 loopback, validate signature/issuer/audience/nonce/expiry, DPAPI current-user y refresh rotatorio.

No existe ZDR global: identidad, credenciales, sesiones y datos operativos de proveedores necesitan persistencia. Expiracion logica de sesiones/flows es aplicada al leer, y purga se hace en actividad; no se promete borrado fisico instantaneo. No se suben historial de navegacion, reflexiones ni claves E2EE. El bloqueo local es independiente del login por arquitectura y pruebas del Host; `deviceLinked:false` permanece explicito.

## Limites posteriores a Fase 1

Cloud Run domain mapping sigue Preview y no es arquitectura de produccion final. Costos por uso sin garantia de USD3/mes; escala 0/1 y quotas gratuitas compartidas. No se implementa MFA recovery por email, vinculacion/E2EE/ajustes globales/ordenes remotas: esas son fases posteriores. Instalacion/experiencia completa del Host con cuenta del propietario sigue disponible como smoke adicional; la biblioteca nativa ya paso aceptacion HTTPS real.

## Estado

Fase 1 aceptada: 61 checks nativo/API, 16 BFF, reinicio real, 7 HTTPS nativos y pruebas publicas de registro/email/reset/OTP/perfil/logout. Scripts parseados, diff y graphify revisados. Siguiente tarea: Fase 2, vinculacion y E2EE, con plan propio.
