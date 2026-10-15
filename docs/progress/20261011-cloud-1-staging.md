# CLOUD-1 — Checkpoint 2: staging en proveedores seleccionados

Actualización: servicios y BFF más recientes están en [checkpoint 3](20261012-cloud1-bff-oidc.md); Host/React y el instalador en [checkpoint 4](20261012-cloud1-desktop-account.md). El estado de este documento es histórico.

Estado: en curso. Fecha del propietario: 2026-10-11 (America/Guayaquil).

## Autorización y alcance

El propietario seleccionó Keycloak y API en Google Cloud, PostgreSQL en la organización Neon `org-falling-boat-78153429` y portal en el equipo Vercel `stuarts-projects-bdd60b3d`, e indicó «continúa». Se mantiene el objetivo de usar planes gratuitos. Crear únicamente recursos FocusGuard de staging, sin alterar proyectos/aplicaciones existentes. No cambiar el motor local Windows ni implementar bloqueo remoto en esta fase.

## Plan

1. Inventariar accesos CLI y servicios existentes; elegir nombres separados.
2. Preparar Neon de staging, bases y roles separados para identidad/producto/BFF.
3. Implementar contenedor Keycloak 26.8.0 de producción con sesiones persistentes y modo stateless, configurar HTTPS/proxy/PKCE, verificar supervivencia a reinicios usando PostgreSQL real.
4. Implementar API ASP.NET Core de identidad con JWT, audiencia, correo verificado, errores sanitizados y health limitado.
5. Crear portal/BFF con login externo, cookies Secure/HttpOnly, CSRF y sesiones/tokens en servidor; página de cuenta y seguridad.
6. Validar localmente y preparar despliegues separados min=0/max=1; revisar cuotas/costos compartidos antes de activar recursos que puedan facturar.
7. Verificar staging HTTPS, documentar límites SMTP/MFA y continuar integración nativa posterior. No declarar toda Fase 1 completa por health o login web.

## Implicaciones

Neon almacena identidad y sesiones necesarias; no hay ZDR total. No transmitir perfiles, reflexiones, navegación ni claves E2EE a identidad. No imprimir credenciales o connection strings ni guardarlas en Git. El servicio local conserva expiración/recovery sin depender de nube. No compartir cookies ni tokens con React. Los dominios automáticos de staging sirven como hosts exactos; sin wildcard de callback.

## Accesos observados

Neon CLI autenticada: organización solicitada en plan Free, tres proyectos existentes ajenos. Vercel CLI autenticada como estuardo666 en el equipo solicitado, sin proyecto FocusGuard previo. Gcloud cuenta transcitynorwalk@gmail.com, proyecto `project-f1fc0332-b76b-4cc7-a8e`; servicio `city-listing` y registro `cloud-run-source-deploy` existentes, a preservar. API de presupuestos no habilitada; no asumir que una alerta de presupuesto es un corte de gasto.

Keycloak 26.8.0 fuente oficial: `STATELESS` es DISABLED_BY_DEFAULT (no EXPERIMENTAL); `PERSISTENT_USER_SESSIONS` DEFAULT. Hay que probar estado de login/reinicio y no solo sesiones terminadas. Docker Desktop existente se inició para ensayos locales; no se instaló software ni servicio nuevo.

## Avance 2026-10-12

Base Neon Free creada por integración Vercel, Auth Neon desactivado; project twilight-dust-71788827. Tres bases/roles aislados y BFF schema real. Credenciales únicamente vault DPAPI local. Portal focusguard-portal-staging desplegado en proyecto propio, HTTP200; auth/me503 sanitizado por configuración pendiente. No tocar servicios existentes.

Keycloak contenedor producción 26.8/stateless compila. Con Neon TLS se creó schema/realm, y pasó login/PKCE/validaciónJWT/logout nativo. API .NET10 compila; ensayo real acredita readiness, rechazo anónimo, ID token y firma alterada. Access token tiene audiencia explícita. Política email se comprueba finalmente en userinfo porque su claim puede no estar en access token. Corrección aún no aceptada: siguiente ensayo cortado por caída Docker Desktop/WSL `exit status 0xc00000fd`, `ResponseEnded`. No afirmar 41 tests completos de esta última ejecución ni API account/revoke PASS.

Portal: 5 tests criptografía/cookie/origen/CSRF PASS; audit0 reportadas. Build Next fallaba EISDIR/readlink en exFAT; copia NTFS + webpack compiló y Vercel confirmó deployReady. Framework Next explícito corrigió primer deploy404; HTTPS200 real comprobado. No login público todavía.

Limitación operacional: Docker falló y no se pudo detener fixture vía Docker CLI en finally. No matar procesos ajenos ni resetear WSL; recuperación/cleanup del contenedor propio pendiente cuando el motor vuelva. Archivo import se elimina en finally; seed sintético protegido localmente. Archivos cloud fuera de solution/installerWindows, producto local sin modificaciones.

Detalles y próximos gates en cloud/README.md. Continúa CLOUD-1, no completada. Google Cloud no recibió servicios FocusGuard todavía; no publicar antes de resolver validación fallida.
