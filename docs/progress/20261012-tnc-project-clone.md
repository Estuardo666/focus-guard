# Clon de staging Google Cloud a TNC

## Plan autorizado

El propietario pide conservar Google Cloud bajo transcitynorwalk@gmail.com, clonar el proyecto con un ID tnc y agregar estuarlito@gmail.com como colaborador especifico. ID elegido: tnc-staging-2026; nombre visible TNC-staging-2026; region us-east4. Recursos nuevos usan tnc-*.

1. Crear proyecto y vincular la misma cuenta de facturacion.
2. Reproducir identidades de runtime, secretos activos e imagenes fijadas por digest.
3. Desplegar copia de identidad/API con escala 0/1 y verificar Ready, health y discovery.
4. Agregar colaborador con Cloud Run Developer, lectura de logs/proyecto/imagenes y actAs solamente sobre las dos identidades de runtime.
5. Verificar acceso usando la cuenta del colaborador y actualizar documentacion.

## Seguridad y recuperacion

No copiar Owner/Editor del proyecto anterior ni otorgar administracion de facturacion o IAM al colaborador. Los permisos de despliegue permiten acceso indirecto a secretos de runtime: el colaborador debe ser de confianza. Secretos transferidos en memoria; sin valores en consola, archivos o Git. El proyecto anterior permanece operativo como respaldo, sin borrado ni cambio de DNS.

Neon y los dominios del producto se mantienen compartidos. Esta copia NO crea una base de datos independiente. El nombre de producto, realm y clientes OIDC permanece FocusGuard; tnc es el prefijo de identificadores de infraestructura GCP.

No clonar automaticamente los mapeos de dominio: las mismas direcciones no pueden trasladarse sin resolver antes la propiedad verificada y planear el corte. No conceder acceso a Search Console en este paso.

## Estado

En curso. Proyecto creado (numero 667080489440) y vinculado a la cuenta de facturacion existente. API necesarias habilitadas. Validaciones y resultado final pendientes.

## Costos

La copia conserva min0/max1; mientras ambos proyectos existan hay almacenamiento y secretos duplicados. La cuota gratuita es por cuenta de facturacion para varios servicios. No hay tope monetario confirmado.

## Resultado verificado

Test-TncStaging.ps1: 44/44 PASS. Ambos servicios Ready, API live/ready 200 y account anonimo 401; discovery conserva issuer. Digest de ambas imagenes identico a origen. Permisos verificados con token de estuarlito: despliegue/logs/proyecto y actAs funcionan; administracion IAM, borrado de proyecto, asignacion de billing, lectura directa de secretos y tokens de runtime no concedidos. Lectura del repositorio individual funciona; listar todos los repositorios del proyecto esta denegado por el alcance limitado. Evidencia sanitizada artifacts/cloud-phase1/staging/tnc-clone-verification.json.

Se actualizo el proyecto por defecto de GCloud; cuenta activa Transcity. ADC independiente no pudo cambiar quota project por falta de serviceusage.services.use; no se ampliaron permisos para sortearlo. Scripts especifican proyecto. Consulta domains list-user-verified fallo por dependencia App Engine/region de ese comando; no prueba que el dominio este verificado. Search Console y traslado de dominios siguen pendientes.

Solo scripts/configuracion/documentacion cloud modificados: sin cambios a binarios distribuidos; no requiere nuevo instalador por este checkpoint. Sintaxis y graphify se validan al cierre.


## Dominios: avance posterior

Search Console en Chrome muestra estuarlito@gmail.com como propietario verificado de totemmassmedia.com por DNS. No se agrego a Transcity como propietario. Se concedio el rol personalizado tncDomainMapper exclusivamente con run.domainmappings.create/get/list al colaborador, para usar su verificacion existente. Ambos DomainMapping creados bajo estuarlito en tnc-staging-2026, ruta auth a tnc-identity-staging y api a tnc-api-staging. Google paso de PermissionDenied a CertificatePending, indicando CNAME ghs.googlehosted.com. cPanel confirmo guardado de ambos CNAME TTL300. Primer intento auth fallo por serial DNS antiguo; tras refrescar zona ambos guardados con exito. HTTPS y flujos de login/email siguen pendientes de verificar.


API authority corregida en revision tnc-api-staging-00002-tsb: URL exacta del realm /realms/focusguard. El valor clonado anterior era la raiz de Keycloak, incompatible con issuer/userinfo exigidos por Program.cs. Sin cambio binario. Test-TncStaging actualizado para comprobar el issuer correcto.


Portal preparado otra vez en NTFS: npm ci, 5/5 tests criptograficos y Next build PASS. Sin desplegar aun. DNS CAA consultado via dns.google: no restricciones CAA en dominio/base FocusGuard. Certificados siguen CertificatePending en comprobacion posterior. README actualizado para reflejar SMTP ya conectado, variables Vercel guardadas y dominios mapeados.


## Espera verificada de certificados

Proceso temporal de 10 consultas cada 60s, 2026-10-12 12:57-13:07 UTC, terminado exit2 CERTIFICATES_STILL_PENDING. Todas 0/2 Ready. HTTPS con validacion TLS normal sigue fallando en discovery auth y health API. Bloqueo externo recurrente: emision Google. No se habilito registro ni se anuncio login publico. Reanudar tras cambio externo consultando DomainMapping y comprobando HTTPS; luego deploy portal y pruebas de registro/verificacion/reset/MFA/nativo/logout.


Usuario completo password y portal muestra cuenta verificada, nombre y deviceLinked:false. Consola Keycloak muestra password creado y opciones de MFA/perfil/aplicaciones. Handoff solicitado para alta TOTP. Suite local anterior fallo: Docker exit1 OOMKilled=false, ERROR directory not found al importar bind E exFAT. Espera identificada por proceso y detenida; archivo fixture eliminado. Script movido a import NTFS y suite real relanzada (en curso).


MFA visible en account console: credential OTP PC W10 creada por usuario. Logout real portal probado: cuenta -> desconectado -> login pide Email/Password, sin SSO automatico. Reset solicitado desde Forgot Password para estuarlito autorizado; Keycloak presenta respuesta generica 'You should receive an email shortly'. Handoff pendiente para reset password y login con MFA. Suite NTFS avanza: 59 checks nativo/API/DPAPI pasan; BFF/reinicio aun en curso.


BFFOnly repeticion PASS: 16 checks y persistencia de realm/reinicio real, exit0. Ronda completa anterior 59 nativo/API PASS, BFF fallo HTTP400 antes de callback; diagnostico agregado sin imprimir cuerpos, rerun BFF paso. Usuario reset desemboco en setup OTP: flujo reset credentials tenia Reset - Conditional OTP / Reset OTP REQUIRED. Credencial PC W10 sigue presente; deshabilitada rama OTP para que email reset solo password, no segundo factor. Config script corregido. Usuario debe validar login password nueva + OTP anterior. Registro permanece cerrado hasta cierre de esta prueba.

