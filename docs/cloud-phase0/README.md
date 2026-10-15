# Fase 0 — FocusGuard Cloud: contratos y decisiones

Fecha: 11 de octubre de 2026. Repositorio: E:\FocusGuard\FocusGuard-Autopilot.
Referencia inspeccionada: commit 5b74b46; el plan tenía cambios locales previos.
Estado: **Fase0 completada: contratos, decisiones, selección y spikes**. Dictamen y evidencia requisito por requisito en [auditoría de cierre](COMPLETION-AUDIT.md). No equivale a backend/bloqueo remoto implementados ni a seguridad productiva certificada. Autorizaciones de código/VM cumplidas; equipos físicos ajenos no modificados.

Actualización: propietario autorizó instalar Android virtual; VM15/API35 instalada, abierta y APK validado en entorno x86_64. Criptografía libsodium1.0.22/JNA5.17 ejecutada dentro de Android, CRYPTO_PROBE_PASS observado. Esto desbloquea ensayos generales sin afirmar Samsung/ARM64. [Instalación y evidencia](../progress/20261011-android-vm-setup.md), [abrir/estado/apagar](../../prototypes/cloud-phase0/android/ANDROID-VM.md).

## Documentos entregados

- [ADR 0016: autoridad y entrega](../adr/0016-cloud-local-authority-and-delivery.md)
- [ADR 0017: identidad y membresía](../adr/0017-cloud-identity-and-device-membership.md)
- [ADR 0018: E2EE y retención](../adr/0018-cloud-e2ee-and-scoped-retention.md)
- [ADR 0019: Strict, consentimiento y recovery](../adr/0019-remote-strict-consent-and-recovery.md)
- [ADR 0020: selección de sobres firmados libsodium](../adr/0020-signed-libsodium-key-envelopes.md)
- [ADR 0021: baseline y frontera de adopción](../adr/0021-phase0-dependency-baseline.md)
- [Licencias/notices seleccionadas](DEPENDENCY-NOTICES.md)
- [Contratos OpenAPI 3.1, envelopes e IPC](CONTRACTS.md)
- [Flujos UX escritos](UX-FLOWS.md)
- [Plataformas, dependencias y ZDR](PLATFORMS-AND-DEPENDENCIES.md)
- [Checkpoint de trabajo](../progress/20261011-cloud-0-contracts-decisions.md)
- [Plan de alcance](../PLAN-CLOUD-MULTIDISPOSITIVO.md)

Los ADR formalizan el diseño solicitado; no declaran aceptación de un cambio instalado ni sustituyen automáticamente SPEC local. El texto de provisión Strict debe integrarse explícitamente en SPEC cuando se autorice implementación, sin simular guardado de recovery.

## 1. Inventario actual observado

| Área | Evidencia | Implicación |
|---|---|---|
| SDK | global.json 10.0.100, latestFeature | Configuración .NET10, no prueba SDK instalado de todos los equipos |
| Lenguaje | Directory.Build.props C#14/net10.0/nullable/warnings-as-errors | Reusar núcleo y contratos |
| UI Windows | FocusGuard.Host.Windows.csproj WinForms/WebView2; UI package 0.1.148 | Shell React actual; Avalonia anterior no es WebView2 portable |
| Dependencias | Directory.Packages.props WebView2 1.0.4258.31, Avalonia 12.1.0, Sqlite10.0.0 | Son versiones del repositorio, no afirmar última versión global |
| Dominio | Session.cs ventana manual 1min–30d; strict tipo de sesión | Cloud normal/Strict tiene límites independientes |
| IPC | IpcAuthorizationPolicy.cs roles Desktop/WebViewHost/ProcessAgent/NativeMessaging/Diagnostic | No hay Sync; necesita allowlist/manifest nuevo |
| Scope | OwnerLogonScope: userSid/logonSid/sessionId con evidencia OS | No aceptar SID declarado por JSON cloud |
| Payload | IpcContracts.cs MaxPayloadBytes=64*1024 | Perfil grande requiere compile/reject antes de ticket |
| Preparación | ADR0011 / SessionActivationWorkflow | TTL dos minutos y consume-once, nunca reconstruir al perder respuesta |
| Strict UI | StrictSessionCommands.cs | Genera código, confirmation ordinal, RecoverySaved y UnsavedWorkConfirmed por sesión |
| Recovery local | RecoveryContracts.cs | PBKDF2-SHA256 210000, 20bytes entropía, hash salado; no sustituir por clave cloud |
| Servicio instalado | Win32_Service lectura: Running/Auto/LocalService | No se reinició, detuvo o modificó |
| UI instalada | VersionInfo 1.0.0+cb14edbe... | No usar FileVersion 1.0.0 para inferir paquete 0.1.148 instalado |
| Grafo | GRAPH_REPORT built cb14edbe; muchos artefactos | Fuentes/ADR actuales prevalecen frente al grafo desfasado |

### Rutas relevantes

- Normal/Strict: UI → bridge validado → SessionWorkflow/ReviewedSessionControlClient → IPC autenticado → InMemoryFocusService → distribución/persistencia → canales.
- React no posee recovery ni autoridad de enforcement por sí solo; bridge Strict muestra credencial durante review local actual.
- Remoto propuesto: sync agent usuario → contrato Sync dedicado → motor. No red hacia servicio LocalService ni scripts arbitrarios.
- Strict remoto necesita provisión local propia y tickets: no reutilizar los booleans del bridge como bypass.

## 2. Evidencia de pruebas existente y su límite

| Evidencia revisada | Qué apoya | Qué no acredita |
|---|---|---|
| StrictPolicyPersistenceTests | Reconciliación/expiración/integridad/reloj controlado | Strict cloud ni comportamiento de reloj físico ahora |
| EnforcementRestartTests | Reinicio de instancia y política | Reinicio físico de cinco OS |
| StrictSessionEndTests | Expired/Recovered y cierre historial | ACK cloud |
| DelayedRecoveryTests | Credencial/demora de recuperación | Provisión reusable remota |
| FocusCycleServiceTests | Prepared/cycles | Protocolos cloud implementados |
| IpcAuthorizationPolicyTests / EvidenceBoundaryTests | Roles/evidencia/scope actuales | Autorización Sync que no existe |
| FG410 strict reboot | Registro de reinicio Strict y liberación; JSON de evidencia existe | No repetido ahora; registro indica recovery no guardado en ensayo corto, por lo que no prueba recovery utilizable |
| FG410 normal reboot | Registro no restauración normal + interrupción; JSON existe | No repetido ahora; no se traslada automáticamente a Linux/Android |

No se ejecutaron tests de producto ni se inició bloqueo durante esta fase documental. Lectura de tests no equivale a tests pasando en el estado actual. Se verificó presencia de JSON anteriores, no se certificó íntegramente su contenido ni nueva instalación.

## 3. Decisiones cerradas en documento

1. Cloud coordina; motor local manda sobre aplicación, salud, expiración y recovery.
2. Intenciones finitas y firmadas, outbox/idempotencia, snapshot inmutable, ACK por destino.
3. Keycloak OIDC; device membership y proof aparte; bootstrap explícito sin circularidad.
4. E2EE de contenido, JCS y revisión CAS antes de cifrado; key wrapping interoperable con gate.
5. Strict remoto en cinco clientes, consentido localmente y con preparación/commit; Apple siempre simulado.
6. Normal reboot Interrupted sin reactivación; Strict reboot restaura solo ejecución íntegra ya aceptada.
7. Recovery login, E2EE y bloqueo son tres procesos distintos; ninguno sustituye otro.
8. Retención cero acotada a categorías no recopiladas/contenido descifrado cloud; no ZDR total.
9. Rol Sync no hereda privilegios UI ni acepta recovery provisionado desde cloud.
10. Preparación/commit parcial no promete atomicidad ni rollback que desbloquee Strict.

## 4. Reglas para desbloquear ambigüedades

| Evento | Decisión normativa |
|---|---|
| Destino normal tarde con 35 s restantes | Puede aplicar ventana restante ≥1s con permiso; nuevo IPC remoto, no duración manual menor de 1min |
| Destino Strict sin recovery guardado | No ticket; provisión local o preparación interactiva |
| Perdida ACK tras aplicar | Reconciliar ledger/motor con mismo ID, no preparar de nuevo |
| Ticket vencido | Rechazar; confirmar localmente adhesión o dejar pendiente |
| Cancel vs commit | CAS un ganador; cancel no revierte committed |
| Revocación durante Strict | Niega sync/tickets futuros; fin/recovery local preservados |
| Reset sin kit | Generación nueva, anclas no sustituidas silenciosamente |
| Borrado de cuenta | Cloud purge, motor conserva autoridad mínima hasta final |
| ACK Strict= true / Expired | Tipo Strict no implica bloqueo; observar estado/end/canales |
| Android pierde permiso | Limited/degraded/unknown; no mentir ni forzar privilegios |

## 5. Auditoría de requisitos Fase 0

| Ítem del plan | Resultado documental | Evidencia pendiente para completarlo |
|---|---|---|
| 1. Inventario/rutas/IPC/pruebas | Revisado y documentado arriba | No se necesita repetir drills para el inventario, pero las nuevas capacidades necesitarán pruebas |
| 2. ADR cloud/local/E2EE/identidad/consentimientos/reboot | ADR0016–0019 creados | Integración SPEC y gates de implementación declarados; no alterar instalación |
| 3. OpenAPI/envelopes/canonicalización/límites/errores | OpenAPI31 operaciones/57 schemas validado con swagger-parser; AEAD/firma/sobres/JCS C#/Node y casos negativos ejecutados | Clientes físicos y codec/almacenamiento completo deben validar en fases siguientes; no afirmar gateway productivo |
| 4. UX login/settings/dispositivos | Flujos/errores/accessibility completos en documento | Pantallas y pruebas físicas no creadas |
| 5. Bibliotecas mantenidas/licencias compatibles | NSec26.4.0/libsodium1.0.22, wrappers0.8.4 y canonicalize5.1.0 fijados; ADR0020 cierra sobres; referencia JCS C# con revision/licencias explícitas y vectors cruzados | Wrapper/ABI Android y revisión de mantenimiento/adopción JCS para producto; licencia raíz/notices/transitivas antes de distribución |
| 6. Spike Android/shell Linux | APK capacidades/nativos instalado y ejecutado en Android15virtual; CryptoProbe pasa con1.0.22; Unix/shell WSL reales pasan | OEM/ARM64/enforcement/recovery futuros no acreditados por ese spike |
| 7. Acceso hardware/opción PWA | Windows hermano/S24/iPhone informados, UbuntuWSL2 confirmado y Android virtual disponible por autorización | Samsung sigue no disponible, Mac no reportado; probes virtuales separados de físicos |
| 8. ZDR/inventario de persistencia/proveedores | Matriz por componente | Config/proveedor no contratado; validación runtime futura |

**Puerta de cierre revisada contra el alcance original:** ADR0021 cierra selección técnica5; los spikes6 se ejecutaron en Linux/Android virtual autorizados; el inventario7 registra acceso/ausencias reales y opciónPWA. La auditoría de cierre conserva los límites OEM/físicos, distribución/adopción endurecida, vault y clientes completos en sus fases originales. No presentar esos pendientes productivos como hechos cumplidos ni bloquear Fase0 esperando construir todo el producto.

## 6. Historial de la validación documental inicial

Validación ejecutada en esta entrega: JSON OpenAPI parseado, 31 operationId únicos, 53 schemas cerrados, 380 refs resueltas, 8 documentos Markdown y 12 enlaces locales existentes; cero errores en la comprobación estructural. No se usó un validador OpenAPI completo ni se ejecutó crypto/enforcement. SHA256 de 582 archivos tracked bajo src/tests/extension/scripts comparados entre lecturas: cero cambios.

- Revisar contratos semánticos y resolver contradicciones con el plan principal.
- Revisión adicional realizada: lectura de pairing pendiente exige prueba firmada del solicitante, sin exigir una sesión activa todavía. JSON actualizado: 383 referencias resueltas, 31 operaciones y 53 schemas, sin errores estructurales.
- Verificar parseo, refs, operationId, headers de seguridad y schemas cerrados del OpenAPI embebido.
- Mantener traceabilidad de pruebas pendientes; no instalar SDKs ni activar dispositivos para hacer aparecer un gate verde.
- Elegir proveedor/region después de comparar costes y privacy; no desplegar durante esta fase.

## 7. Trabajo autorizado y límites actuales

Para terminar ítems ejecutables: prototipos aislados AND-0/LIN-0, fixtures de interoperabilidad crypto y pruebas físicas consentidas. No hay autorización implícita para cambiar permisos OS, instalar apps en equipos ajenos, guardar secrets en repo ni desplegar cloud.

La autorización para prototipos aislados ya llegó. Estado vigente: OpenAPI31 operaciones/57 schemas validado, 33 pruebas de contratos/admisión/cifrado y probes reales Unix/shell en WSL. Android tiene APK compilado/firmado pendiente de ejecución física. Sobres seleccionados por ADR0020, HPKE provisional sustituido. Continúan pendientes adopción nativa Android, pruebas físicas y gates de producto; no presentar resultados de Node/WSL como pruebas en Samsung o Safari.

## 8. Primer incremento ejecutable

Última auditoría ejecutable: 33 tests pasan, build0 errores/warnings y OpenAPI31 operaciones/57 schemas validado. Solicitudes normal/Strict separadas por schema; ventanas de admisión respetan máximo local y rechazan relojes/estado desconocidos; reservas no reviven ni cambian estados terminales. SDKs/producto no se modificaron por esas reglas de prototipo.

Dependencia Android: auditoría offline de AAR5.2.0 y los cuatro ABI ejecutada, publicación verificada, licencia/transitivas registradas. Bundle incluye cadena libsodium1.0.20; mantener gate de producción cerrado hasta actualizar/revisar runtime. Libsodium1.0.22 Android fue observado como candidato oficial en assets, sin ejecutarlo. No presentar APK de capacidades o tests C#/Node como validación de esa dependencia Android.

[Código aislado y comandos](../../prototypes/cloud-phase0/README.md). Build NSec: cero errores/warnings. OpenAPI swagger-parser 13.1.0 pasó; 20 tests Node pasaron; prueba Python Linux pasó. npm audit aislado y consulta NuGet de dependencias no reportaron vulnerabilidades en ese momento; no certifican seguridad total del protocolo.

WSL observado al ejecutar: kernel Linux 6.18.40.1-microsoft-standard-WSL2, Python3 presente, DISPLAY/Wayland de WSLg presentes, dotnet/node/secret-tool no encontrados en PATH Linux. No se concluye soporte GUI/enforcement/keyring a partir de esas variables. SDK Android, Android Studio y Java no encontrados en ubicaciones habituales inspeccionadas; planificar toolchain aislada antes de compilar Android.

Se preparó toolchain portable en artifacts (JDK21 y Android35/build-tools35, sin instalación global). [APK local](../../artifacts/cloud-phase0/android-probe/focusguard-phase0-test.apk), warnings-as-errors y firma v2/v3 verificada. SHA256 inicial A3221C3F14AE0657F3DAA8095432B91B80C822B72D9422E98E6AD8B4C6A5549A; APK de cierre con JCS/nativos0F5F440E8521B724A98B902E3C00F502103E1F72F84E8A39E0D58F29D8594770 instalado únicamente en VM autorizada. INTERNET/enforcer no incluidos; ningún teléfono físico modificado.

NSec ejecutado realmente en Windows y WSL produce iguales ciphertext, Ed25519 public key y firma; en WSL se usó globalization invariant únicamente para probe por faltar ICU. El shell Avalonia12.1 self-contained inicialmente falló por libICE.so.6 ausente. Tras descargar libICE/libSM mediante repositorio Ubuntu y extraerlos en directorio temporal Linux (el montaje Windows no admite sus symlinks), el runner real imprimió SHELL_WINDOW_OPENED/SHELL_EXIT_NORMAL y terminó exit0. Sin apt install ni mock headless. No cambiar la aplicación a invariant: internacionalización, accesibilidad y recuperación siguen pendientes.

Sealed boxes libsodium C#/Node funcionan en ambas direcciones y rechazan alteración. ADR0020 selecciona esa alternativa de ADR0018 como FG-SODIUM-BOX-1, con firma externa y binding de payload/headers. No es HPKE; no hay datos cloud persistidos que migrar. La suite actual tiene 33 pruebas pasando, incluida JCS C#/Node con Unicode/números/duplicados y UTF8 inválido. Samsung no está disponible según el propietario: su ejecución física queda pendiente sin fingir resultado positivo.

