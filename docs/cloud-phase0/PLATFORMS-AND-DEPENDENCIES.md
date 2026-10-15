# CLOUD-0 — Plataformas, dependencias y privacidad

Fecha: 2026-10-11. Inventario documental inicial y resultados de prototipos aislados posteriores, autorizados por el propietario. Los estados iniciales de la tabla se conservan como historial; resultados actuales abajo.

Estado final: selección cerrada en ADR0021 y auditoría de cierre Fase0. Android15virtual autorizado ejecutó motor1.0.22/JNA5.17 y JCSJava, sin bundle viejo. Fuente/hashes/ABI y obligaciones están documentados; hardening, ARM64/OEM y distribución pública permanecen en fases posteriores, no como un hecho ya probado.

## Resultados ejecutados tras autorizar código

- NSec26.4.0 (libsodium1.0.22) y libsodium-wrappers0.8.4: AEAD/firma interoperables; sobres sealed boxes nativos C#/Node en ambas direcciones. 20 tests totales pasan; HPKE continúa pendiente y no se declara equivalente a sealed boxes.
- canonicalize5.1.0 Apache-2.0 y swagger-parser13.1.0 MIT/Ajv8.20.0 MIT: paquetes fijados con lockfile, validados en Node24.19.0. Aún sin JCS .NET/Android probado.
- Ubuntu WSL2 fue arrancado para ejecutar pruebas: Python SO_PEERCRED/Unix socket y permisos reales pasaron. C# crypto portable pasó con invariant por faltar ICU. Shell Avalonia12.1 inicialmente falló por libICE.so.6; con libICE/libSM extraídos en directorio temporal abrió/cerró realmente, exit0. No apt install ni headless. Se requieren esos prerrequisitos para packaging Linux posterior.
- JDK21 Microsoft y platform/build-toolsAndroid35 descargados/verificados/extraídos en artifacts, sin instalación global. APK Java de capacidades compilado sin warnings, firmado con certificado de test y verificado. No representa el cliente Kotlin final ni realiza bloqueo; prueba en S24 pendiente del dueño.
- Herramientas Android oficiales comparadas contra checksum publicado en repository2-1.xml; hashes del ZIP JDK/plataforma/build-tools registrados en checkpoint. No SDK/emulador/servicio instalado en la máquina ni APK instalado en teléfonos.

## Hardware y herramientas

| Recurso | Evidencia | Qué permite | Qué falta |
|---|---|---|---|
| Windows local | Repositorio y herramientas dotnet/node observados | Inventario código y contratos | No se inició bloqueo ni se verificó instalación actual |
| Otro Windows del hermano | Declaración del propietario en este chat | Futuro ensayo de dos equipos | Consentimiento local, versiones y conexión; sin acceso remoto solicitado |
| iPhone 15 Pro Max propio | Declaración del propietario | Futuro Safari/PWA de simulación | Prueba física pendiente; no inspeccionado |
| Samsung S24 del hermano | Declaración del propietario | Futuro Android físico | Consentimiento del dueño, versión OS/permisos, app de ensayo |
| WSL2 Ubuntu | wsl --list --verbose: Ubuntu Stopped, versión 2 | Lógica/API/contratos y transporte de prueba | No se arrancó distro ni se afirmó versión Ubuntu/GUI |
| docker-desktop WSL2 | Listado observado Stopped | Potencial staging local aislado | No se inició servicio Docker ni contenedores |
| Mac | No se comunicó uno disponible | Companion web diseñable | Hardware/navegador macOS pendiente |

ADB y Java no se localizaron en PATH; no equivale a demostrar que no están instalados. No se instalaron ni habilitaron USB/debug. Tener WSL no prueba enforcement Linux instalado, logout/reboot, recuperación, compositor ni keyring.

## Selección de dependencias

| Función | Selección de diseño | Fuente/licencia | Gate de adopción |
|---|---|---|---|
| Identidad | Keycloak autoalojado | [repo/Apache-2.0](https://github.com/keycloak/keycloak) | Config producción, CVE/versión soportada, tests PKCE/refresh/MFA |
| .NET contenido y firmas | NSec.Cryptography | [repo/MIT](https://github.com/ektrah/nsec) | .NET10, RID win-x64/linux-x64/arm64 y vectors XChaCha/Ed25519 |
| Web/PWA contenido | libsodium-wrappers | [repo/ISC](https://github.com/jedisct1/libsodium.js) | WASM/CSP, bundle local firmado, vectors y Safari real |
| Android contenido | Probe: libsodium1.0.22 + adaptador JNA5.17; app staging: JNA5.19.1, ADR0021 | ISC + alternativa Apache2 de JNA | AVD x86_64 y pairing canonical pass; Android16/ARM64/Samsung/page-size/distribución siguen pendientes |
| Key wrapping | libsodium sealed-box + firma Ed25519, ADR0020; HPKE provisional sustituido | [API/bindings oficiales](https://doc.libsodium.org/bindings_for_other_languages) | C#/Node probados; módulo interop endurecido y Android/ABI por validar antes de Fase2 |
| Canonicalización | canonicalize5.1.0 y referencias C#/Java upstream32a4819 | [RFC8785](https://www.rfc-editor.org/rfc/rfc8785.html), notices locales | Vectors C#/Node/Android pasaron; ownership y actualización fuente segúnADR0021 |
| Shell Linux | Avalonia 12.1 existente | [documentación Linux](https://docs.avaloniaui.net/docs/platform-specific-guides/linux) | Probar accesibilidad/compositor/paquete reales; no XPF comercial asumido |
| Transporte y API | ASP.NET Core/SignalR + PostgreSQL | Stack .NET del proyecto | Proveedores/runtime fijados al implementar; sin microservicios |

No se localizó archivo LICENSE raíz en el inventario. Por tanto, verificar compatibilidad con la licencia real elegida por el proyecto antes de distribuir, conservar notices y revisar transitivas. Esta tabla no certifica legalmente compatibilidad ni mantenimiento por encontrar un repo.

Sodium.Core no se selecciona para proyecto nuevo: su propio [README](https://github.com/ektrah/libsodium-core) declara mantenimiento reducido y recomienda otros bindings. NSec sí expone primitivas de contenido, pero no se presume que implemente sealed boxes o HPKE; no mezclar formatos por nombre de algoritmo.

**Decisión actual:** ADR0020 selecciona crypto_box_seal oficial con firma externa y codec JCS, sustentado por 26 pruebas. No se implementaron KEM/KDF/cipher propios. Pendientes JCS .NET/Android y módulo interop/ABI de producto: Fase2 no habilita datos reales hasta validar los tres clientes. El candidato Android sigue sujeto a mantenimiento/licencia transitiva y prueba física; Samsung no está disponible ahora según el propietario.

## Evaluación Android documental

### Auditoría de artefacto y mantenimiento ejecutada

Repo terl/lazysodium-android no archivado; push/commit último observado31-05-2025, commit76e18548a0af285215f536c0d6d17369ac598311. Maven Central publica5.2.0 con POM MPL2 y dependencias JNA5.17.0, AndroidXcore-ktx1.16.0 y Kotlinstdlib2.1.21. AAR SHA256 B5378C1D9DB2573D61B304E89CF83DB187A05C3E4FF081D9B2EF3D0BB00CA314, checksum de publicación verificado; inspección ELF confirmó arm64-v8a, armeabi-v7a, x86 y x86_64. Todos contienen cadena1.0.20; no es ejecución ni certificado de parche.

[Changelog oficial libsodium](https://raw.githubusercontent.com/jedisct1/libsodium/master/ChangeLog) documenta corrección de validación de puntos Ed25519 posterior a esa versión. No afirmar explotabilidad del binding sin analizar llamadas/parches; tampoco aprobar bundle antiguo por existir un ABI correcto. Assets libsodium1.0.22 ISC restaurados vía NSec contienen los cuatro RID Android con versión1.0.22 y arquitectura esperada, como candidatos de actualización. El nuspec advierte que ese paquete es implementación interna: no referenciarlo como API Android directa. Revisar wrapper/actualización, notices, ABI/alignment y vectors runtime antes de selección productiva final.

Auditoría reproducible: prototypes/cloud-phase0/android_dependency_audit.py. Resulta runtimeValidated=false y gate de producción bloqueado; la compilación del APK de capacidades no utiliza esta biblioteca ni cambia por este hallazgo.

DevicePolicyManager.setPackagesSuspended no es capacidad de cualquier app personal: roles administrativos específicos, fuera de MDM del producto. UsageStats no suspende apps. AccessibilityService requiere disclosure/consent/políticas; no leer contenido de pantalla. Overlay/launch background están limitados; no prometer ejecución inmediata ni evitar force-stop.

Fuentes: [DevicePolicyManager](https://developer.android.com/reference/android/app/admin/DevicePolicyManager), [Activity security](https://developer.android.com/guide/components/activities/secure-bal), [política Accessibility](https://support.google.com/googleplay/android-developer/answer/10964491?hl=en).
El hallazgo es de viabilidad potencial/limitada, no prueba de bloqueo en S24.

Spike futuro AND-0: app mínima de ensayo con no datos personales; normal/Strict, permiso negado/revocado, ahorro batería, force-stop, reboot, fin sin red, recovery. Solo declarar capacidad después de resultados por canal. No bloquear llamadas/emergencia/settings de recovery. Instalación/permiso los decide el dueño.

## Evaluación Linux documental

No trasladar WebView2/Win32/named pipes/DPAPI al Linux. Reusar Domain/Contracts y shell Avalonia con sockets Unix autenticados, keyring y enforcer por usuario.

Spike futuro LIN-0: WSL para IPC/reconciliación y pruebas de contratos; distro desktop dedicada para X11/Wayland, keyring, extensión, agente, reboot/logout y recovery. WSL bajo Windows no prueba todos esos gates.

## Apple y distribución

PWA sobre HTTPS sin membresía pagada ni bloqueo OS; prueba Safari/iPhone propia primero, macOS cuando haya equipo. Personal Team permite determinadas pruebas nativas, sin convertirlo en permiso para TestFlight/App Store o bloqueo real. [Apple memberships](https://developer.apple.com/support/compare-memberships/).

No ejecutar App Store/APNs/Screen Time ni cobrar membresía ahora. PWA BFF evita refresh tokens en localStorage, pero JS del origen puede acceder a claves E2EE durante uso; CSP reduce riesgo, no elimina compromiso del origen.

## Inventario de retención y ZDR

| Componente | Datos necesarios | Datos prohibidos / captura a desactivar | Retención objetivo |
|---|---|---|---|
| IdP | correo/hash/MFA/sesiones | password/tokens en logs | Config de cuenta + seguridad mínima, documentar purge |
| API/worker | IDs, tiempos, ciphertext, tickets | bodies/keys/reglas en claro | Según tabla 10.6; TTL por clase |
| DB/outbox | ciphertext y routing | documentos descifrados | Fijar snapshots hasta fin + 7 d |
| Proxy/CDN/WAF | controles seguridad mínimos | queries/bodies/cookies/session recording | Sanitizado mínimo; proveedor por validar |
| APM/errores | código/latencia/correlation opaca | payloads, variables sensibles, dumps | 14 d ops / 30 d seguridad mínimos |
| Mail | correo de verificación/reset | reglas de foco/kit | Proveedor no elegido; política pendiente |
| Push opcional | token + referencia opaca | perfil, sitios, password | Mientras device activo; eliminar al revocar |
| Backups | copia cifrada necesaria | secretos E2EE/plaintext | 30 d y supresiones reaplicadas |
| Clientes locales | claves vault/política/recovery | subida de historial/navegación | Local según sesión/consentimiento |

No ZDR global ni promesa sobre proveedores sin contratos/configuración. Categorías nunca recopiladas y contenido E2EE descifrado tienen retención cloud cero por diseño; auditar secundarios. [OWASP logging](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html).

## Plan de evidencia pendiente

AND-0 y LIN-0 exigen prototipos/ejecución de pruebas que no están autorizados bajo «no editar código»; describirlos no los completa. HW-APPLE-MAC exige equipo macOS. DEP-HPKE/JCS exige fixtures interoperables y paquetes exactos; revisión documental no basta. El cloud aún no está desplegado: no hay login/sync real que probar en teléfonos.

