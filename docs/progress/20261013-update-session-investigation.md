# Sesión desconectada tras actualización — diagnóstico inicial

El propietario informa que reinstaló/actualizó y la aplicación apareció desconectada. Comportamiento esperado: actualización y reinstalación conservando datos mantienen la sesión salvo revocación, caducidad del servidor o credencial inválida.

Plan: revisar preservación de datos del instalador, ubicación del vault, recuperación de sesión y causas de borrado; reproducir antes de implementar.

Observaciones de solo lectura:

- HostComposition usa LocalApplicationData/FocusGuard/identity-tokens.dpapi, independiente de E:/D: y del directorio de instalación.
- Ese archivo no está presente al inspeccionar; sí permanecen dispositivos, perfiles y preferencias. No se leyeron credenciales ni material cifrado.
- Las búsquedas en Setup/install/uninstall no muestran eliminación explícita del vault de identidad. La desinstalación borra binarios y opcionalmente datos de máquina, no este vault de usuario.
- IdentitySession.RestoreAsync borra el vault ante sesión inválida, invalid_grant del servidor, respuesta de renovación inválida o identidad distinta. Errores de red y rechazo de userinfo lo conservan.

No hay evidencia suficiente para atribuir la desaparición al instalador ni para distinguir expiración/revocación de sesión inválida. No se modificó autenticación, configuración de seguridad ni código. Falta capturar/reproducir el siguiente ciclo de actualización con sesión válida y registrar únicamente existencia/hash opaco del archivo y código seguro de recuperación. No declarar corregido.

## Plan de corrección autorizado

Consulta de solo lectura al servidor staging confirmó SSO idle 1800 segundos/max 28800, rotación sin reutilización y cliente nativo sin scope offline_access. Esto explica que la sesión pueda perderse al reiniciar después de superar los límites, aunque no demuestra la causa exacta de la pérdida anterior sin registros.

Corregir el diseño para Windows: solicitar sesión persistente offline_access, límite máximo 30 días, scope opcional solo en cliente nativo, DPAPI y rotación conservadas; revocación explícita del refresh token al desconectar. Portal y Android conservan su política actual. Primero prueba fallida del token corto real; luego restauración/rotación/revocación contra Keycloak real, suites locales y nuevo instalador versionado. No instalar ni pedir credenciales automáticamente.

## Implementación y evidencia

- IdentitySession solicita `offline_access` y usa el endpoint de revocación para cerrar su sesión persistente. El vault DPAPI y la rotación permanecen intactos. No se acepta una sesión por decodificar un JWT: la validación criptográfica sigue siendo obligatoria.
- Native-SessionPolicy y Configure-NativeSessionStaging reconcilian un límite idle/max de 30 días, scope opcional del cliente nativo y el rol básico offline_access para cuentas existentes/futuras. Este rol no concede permisos de producto/API ni administrativos. Android y BFF no solicitan el nuevo scope. Las sesiones web siguen limitadas a 30 minutos/8 horas y el acceso a 5 minutos.
- La regresión de login real falló con la implementación anterior: token corto en vez de Offline. Con la corrección se verificaron token persistente real, restauración tras Dispose/nueva instancia, renovación/rotación, rechazo del token anterior, cifrado y rechazo del token después de logout. No se esperaron 8 horas ni se atribuye sin evidencia la desaparición histórica exclusivamente al instalador.
- En la preparación apareció un problema de enumeración de arrays de Invoke-RestMethod y cuentas importadas sin roles predeterminados. Se corrigió la reconciliación y se verificó la capacidad offline en el flujo real, sin sustituir pruebas por mocks.
- Suite .NET Release completa exit0; React 236/236; extensión 19/19; scripts del instalador PASS.
- Instalador .159 generado y verificado con `--package-self-test`, `--smoke-web-ui` y `--smoke-test` (exit0). SHA256 `D9E422B74C281855F881033E2F5390B01C6822228910B4716BA59F4D00195051`. Los primeros argumentos de comprobación intentados (`--self-test`, `--smoke`) no correspondían al contrato del programa; se corrigió la invocación y se verificaron los procesos con Start-Process/Wait y su ExitCode real. No se instaló.

Fuentes técnicas: https://www.keycloak.org/docs/26.5.7/server_admin/index.html#_offline-access y código del proveedor de refresh 26.8.0 https://github.com/keycloak/keycloak/blob/26.8.0/services/src/main/java/org/keycloak/protocol/oidc/refresh/DefaultRefreshTokenProvider.java . El endpoint de revocación elimina la sesión indicada por el token, no inicia ni cancela sesiones locales de enfoque.

Limitación: una sesión previamente descartada no se puede reconstruir; el propietario debe iniciar sesión una vez con .159 para emitir la nueva credencial persistente. La actualización física en su PC requiere aceptación; pruebas físicas de Fase 2 pendientes conservan su estado.

## Verificación final y estado

- `Test-KeycloakNeon.ps1 -NativeOnly`: exit0, **125 comprobaciones** reales y reinicio del contenedor con Neon persistente PASS. Registro `artifacts/cloud-phase1/native-persistent-session-tests.log`.
- Falló inicialmente la preparación de caducidad de una prueba API: Neon estaba 2,220–2,370 segundos por delante del host, y `NOW()-1 segundo` aún podía estar en el futuro para la API local. Diagnóstico de solo lectura mediante clock_timestamp; la prueba ahora escribe una fecha 5 minutos anterior en el reloj de la API. Rechazo real HTTP403 confirmado, y recuperación con nuevo secreto firmado HTTP200. No se relajó ninguna comprobación de autorización del producto.
- `Configure-NativeSessionStaging.ps1`: exit0. Política aplicada y verificada en realm focusguard del servicio TNC. Conservadas las políticas SSO web, access-token, correo y contraseña; no se alteraron MFA, credenciales ni roles administrativos. Registro `artifacts/cloud-phase1/native-session-staging-policy.log`.
- Paquete contiene Identity.dll SHA256 `34FEBC7D8DDCA374C62B5AA252F300E5E852D01D9FCDFA8FD839F7F9E035886E`. Standalone .159 mantiene SHA256 `D9E422B74C281855F881033E2F5390B01C6822228910B4716BA59F4D00195051`.
- Grafo AST actualizado; git diff --check sin errores. Sin instalación en la PC del propietario.

DEBUG REPORT: síntoma, desconexión después de actualizar; causa verificada, diseño de sesión nativa corta ligado a SSO 30min/8h; no hay registro que pruebe el borrado histórico por el instalador. Corrección, sesión nativa persistente limitada/DPAPI/rotación/revocación; evidencia, regresión fallida previa y 125 comprobaciones reales PASS. Estado: DONE_WITH_CONCERNS (implementación/servidor/paquete listos; falta aceptación física de instalar, iniciar sesión y volver a abrir/actualizar manteniendo la sesión).

## Aceptacion fisica del propietario — 2026-10-13
El propietario confirma que inicio sesion y reinstalo la .159 sobre la instalacion existente sin cerrar sesion. Captura posterior muestra Signed in, correo Verified y dispositivo Linked to this account. PASS para conservacion de sesion y vinculo en reinstalacion sobre esta PC. No prueba una migracion entre versiones diferentes, un reinicio de Windows ni el transcurso de 30 dias; esas situaciones no se declaran verificadas por esta captura. Evidencia proporcionada: codex-clipboard-604e6a63-b744-4ecf-a8ad-0a9a386a409c.png. Este resultado cierra la aceptacion de reinstalacion de este arreglo; las pruebas pendientes de Fase 2 en el otro Windows siguen pendientes.
