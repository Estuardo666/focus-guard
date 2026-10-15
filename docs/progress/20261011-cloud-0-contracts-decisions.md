# CLOUD-0 — Contratos y decisiones

## Dictamen final Fase0 — completada

Los estados en curso/bloqueado posteriores son historial de pasos anteriores, no estado vigente. Cierre en docs/cloud-phase0/COMPLETION-AUDIT.md inspecciona los ocho requisitos originales y frontera de fases. ADR0021 fija baseline/licencias/ownership de dependencias. 33 tests pasan y OpenAPI31 operaciones/57schemas validado; LinuxUnix/shell reales reejecutados exit0. Android15virtual autorizado (no mock) ejecutó libsodium1.0.22/JNA5.17 y referenciaJCSJava: CRYPTO_PROBE_PASS, cipher/firma cross, AAD/box tamper y Unicode/números/orden/duplicados. ABI físico/OEM/fullvault/enforcement y distribución conservan gates de Fases2/6/9, no se atribuyen a cierre arquitectónico.

Inventario hardware recoge fuente humana y ausencia real del Samsung/Mac, con VM alternativa expresamente autorizada; no inventar conexión física. No se cambia rootlicence ni se publica producto. Lectura/diff de src/tests/extension/scripts confirma componentes de Windows intactos. Próxima fase prevista1 identidad/cuenta; no iniciada ni desplegada automáticamente.

## Estado de cierre: bloqueado por evidencia externa pendiente

Revalidado tras tres turnos del recorrido reanudado: el Samsung continúa no disponible según el propietario; APK de ensayo existe pero no hay ejecución física registrada. Matriz de Fase0 todavía muestra pendientes de aceptación en5/6/7. La validación nativa Android no se sustituye por inspección ELF ni por los33 tests C#/Node. No quedan comprobaciones de esos gates que puedan acreditarse mediante otra repetición de las mismas pruebas locales.

Se marca objetivo blocked, no complete ni paused, hasta disponibilidad/evidencia del dispositivo y cierre de los gates de adopción. La autorización de código persiste; no se solicita de nuevo. No ampliar a Fase1, instalar APK remotamente ni declarar Fase0 terminada para evitar el bloqueo. Documentos/prototipos y hallazgos preservados; producto sin diff.

## Ampliación autorizada: prototipos aislados

Auditoría del siguiente turno automático: estado autoritativo revalidado, objetivo activo y Samsung aún no disponible. Detectados request schemas normal/Strict compartidos, contextos NaN/no salud y transiciones tardías que sobrescribían terminales. Corregidos en OpenAPI/prototipo sin tocar producto: 57 schemas, 31 operaciones y 33 tests pasan. Deadline de reserva finito/inmutable y no posterior a end; máximo local se valida sin truncar el fin revisado. Tests de key envelope ahora usan contextos válidos pero distintos para comprobar binding y no solo sintaxis inválida. ADR0016/plan/semántica actualizados. Fase total aún pendiente de gates físicos/adopción, sin redefinir scope ni marcar DONE.

Auditoría independiente restante: Maven Lazysodium5.2.0 POM/estado repo/licencia/transitivas, AAR verificadoSHA y ELF de cuatro ABI. Detectada cadena1.0.20 en todos; consulta upstream documenta fix posterior de validación de puntoEd25519, sin inferir explotabilidad del binding. Comparación contra assetsAndroid1.0.22 de NSec confirma candidatos, no runtime. Script report-only añadido: android_dependency_audit.py, gate productivo bloqueado y runtimeValidated=false. No se usó library en APK ni se tocó paqueteNuGet restaurado; artifactsololectura/candidato. Samsung sigue indisponible; no se pidió de nuevo ni se activó dispositivo.

Estado actual al finalizar este incremento: 29 pruebas pasan y OpenAPI31 operaciones/54 schemas validado. Samsung S24 no disponible según respuesta del propietario; no ejecutar ni asumir prueba física. Sigue pendiente la aceptación total de Fase0 conforme a su matriz, no se avanzó al backend de Fase1.

El propietario autorizó escribir código el 2026-10-11. Restricción de solo documentos revocada para completar Fase 0. Se crean herramientas aisladas en prototypes/cloud-phase0, fuera de FocusGuard.slnx y componentes instalados. Primer incremento: validación OpenAPI/JSONSchema con casos negativos y vectores reales NSec/libsodium Node. No modificar enforcement actual, no instalar apps en equipos del hermano ni servicios del sistema. Prototipos físicos Android/Linux y HPKE continúan como gates independientes.

Resultados del incremento: dotnet build Release del CryptoProbe con NSec26.4.0, cero errores/warnings; 18 pruebas Node pasan (contrato, JCS básico, AEAD/Ed25519 C#/Node, ventanas/admisión/consentimiento/recovery y reservas Strict). swagger-parser13.1.0 validó OpenAPI3.1 sin referencias externas. npm audit aislado: cero vulnerabilidades reportadas. Probe Python real en UbuntuWSL pasó permisos de socket/directorio y SO_PEERCRED contra proceso hijo propio. No se certifican enforcerLinux, GUI, Android, Safari ni HPKE.

Cambios: prototypes/cloud-phase0 con herramientas, tests y lockfiles; README de fase actualiza autorización/resultados. No se modificó ningún componente de producto ni solución/installer. No requiere regenerar installer porque el componente distribuido no cambió. Pendientes: Android toolchain/local build, shell Linux, HPKE/JCS entre clientes y pruebas físicas consentidas.

Continuación del incremento: 20 tests pasan tras añadir sealed boxes libsodium en ambas direcciones C#/Node y rechazo de alteraciones. Formato HPKE del diseño no se cambió por estos resultados. dotnet format aplicado exclusivamente a dos proyectos de prototipo.

Android: JDK21 portable ZIP SHA256 192441A9D27DA813BADA974BB88B4CF64D37A9589ED37F204374D411CA5CE07F. Platform35 SHA256 A93712AEDD4F851D9CF1F7281F7C4FE5581CE03A9DF37FFC8C53A487EADF311F y build-tools35 SHA256 5753C679A1B90BCF6FBC9945A2CE39DFB9E74F1DF0831A1E1866AE5B594326F0 verificados contra SHA1 del catálogo oficial Google. Extraídos únicamente artifacts. Activity Java compilada con javac release17 y warnings-as-errors, dex/aapt2/zipalign/apksigner. APK test firmado v2/v3 SHA256 A3221C3F14AE0657F3DAA8095432B91B80C822B72D9422E98E6AD8B4C6A5549A. Certificado debug desechable local, no producción. Sin INTERNET; únicamente consulta estado propio PACKAGE_USAGE_STATS/overlay. APK no instalado; permiso del dueño/prueba S24 pedidos al propietario.

Linux: prueba de runtime C# Windows/WSL produjo iguales cipher/publicKey/signature. WSL sin ICU abortó el primer intento; modo invariant por proceso permitió probar bytes, no readiness del producto. LinuxShellProbe Avalonia12.1 self-contained compiló cero warnings, ejecución real falló por libICE.so.6 ausente. No instalar paquetes OS ni convertirlo a headless para fingir GUI lista. Estos son resultados reales del spike, con prerequisites documentados.

graphify update . ejecutado tras primeras fuentes: 14189 nodos, graph.json/GRAPH_REPORT actualizados, visualización omitida por tamaño normal de grafo. Actualización final pendiente de completar rondas de prototipo. Componentes de producto sin diff.

Shell Linux resuelto de forma portable: apt-get download libice6/libsm6 sin instalación, dpkg extract en montaje Windows falló por symlinks no admitidos; extracción dentro de TemporaryDirectory Linux funcionó. Runner linux_shell_probe.py produjo SHELL_WINDOW_OPENED/SHELL_EXIT_NORMAL y exit0, directorio propio limpiado al terminar. No sustituye pruebas de enforcement/accessibility/intl. Los paquetes .deb permanecen solo en artifacts para reproducibilidad; SDK global y servicio producto intactos. Última actualización graphify completada: 18588 nodos y 87124 edges antes de añadir runner final; se reejecuta tras cerrar el incremento.

ADR0020 cierra selección de sobres FG-SODIUM-BOX-1, sustituyendo HPKE provisional de ADR0018 con evidencia real de codec firmado/metadata/tamper. OpenAPI añade AccountKeyPayload y mantiene versión de diseño, sin contenido cloud previo que migrar.

JCS C#: NuGet jsoncanonicalizer1.0.0 no adoptado por nuspec sin licencia/source adecuados. Referencia upstream commit32a48195b9b20fe67b1795e39e4509f14d860b45 copiada con notices por archivo Apache/BSD/MPL2/Lucent y textos completos Apache/MPL; no marcada Apache-only ni packable. Primer download detenido al detectar licencia distinta: se revisaron headers antes de aceptar. Un build se ejecutó antes de terminar la transferencia y falló por fuente incompleta; se esperó el handle real y se repitió. Prueba Unicode detectó entrada OEM Windows (caf├®); fijar Console.InputEncoding/OutputEncoding a UTF8 estricto resolvió causa real. 29 tests pasaron con casos no ASCII, JSON duplicado y UTF8 inválido; cero errores/warnings en proyectos propios y de referencia. dotnet format verify pasó excluyendo fuentes vendor intactas.

Archivos de prototipos no afectan instalador/componentes distribuidos. Diff de src/tests/extension/scripts sigue vacío. La prueba Android física fue declarada pendiente al responder el propietario que Samsung no está disponible; no se repitió solicitud ni se instalaron APKs/remotamente.

Fecha: 2026-10-11. Estado: en curso, solo documentación.

## Plan de trabajo

1. Revisar SPEC/ADR, rutas de activación, autorización IPC y evidencias existentes.
2. Formalizar decisiones cloud/local, identidad, E2EE, consentimiento y reboot en ADR nuevos.
3. Escribir contrato OpenAPI 3.1 y semántica de envelopes dentro de Markdown.
4. Escribir flujos UX, privacidad/ZDR, selección de dependencias y evaluación de plataformas.
5. Verificar referencias, estructura de contratos y cobertura requisito por requisito.

## Alcance autorizado

No editar código, instalar servicios, desplegar, iniciar bloqueos ni vincular equipos.
Los spikes ejecutables y pruebas físicas que requieren implementación quedan pendientes explícitos, sin sustituirlos por mocks.

## Hardware comunicado por el propietario

Otro Windows del hermano, iPhone 15 Pro Max propio, Samsung S24 del hermano y entorno WSL.
Acceso físico informado no equivale a vinculación probada. Consentimiento del dueño requerido antes de pruebas en equipos prestados.

## Implicaciones

Seguridad: motor local conserva autoridad y recovery offline; login no otorga por sí solo permiso de bloqueo.
UX: diferenciar real, limitado, pendiente y simulado.
Recuperación: no reutilizar preautorización estricta sin provisionar código local y formalizar el contrato.

## Validación prevista

Lectura de código/test/documentación, fuentes oficiales, parseo del OpenAPI embebido y chequeo de referencias/semántica. Sin builds ni ejecución de enforcement.

## Resultado del turno

Parte documental preparada. Fase 0 total pendiente: no se marca DONE ni se modifica objetivo para excluir los spikes.

Archivos creados: ADR0016–0019, docs/cloud-phase0/README.md, CONTRACTS.md, UX-FLOWS.md y PLATFORMS-AND-DEPENDENCIES.md. Actualizados este checkpoint, TASKS.md y plan principal para traceabilidad.

Contratos: OpenAPI 3.1 embebido en Markdown, 31 operaciones y 53 schemas; bootstrap, pairing confirm, device sessions, documentos CAS, Strict tickets/commit, ACK y privacy/export/delete. Semántica temporal, modo, firma, recovery y IPC escritos; son diseño futuro, no endpoint disponible.

Validación ejecutada con Node desde stdin (sin archivo de código): parseo JSON; operationId únicos; 380 referencias resueltas; parámetros path requeridos; bodies mutantes; schemas cerrados y required válidos. Revisados 8 Markdown, fences/UTF8 y 12 enlaces locales. Resultado: cero errores estructurales. No afirmar validación completa OpenAPI/JSONSchema ni vectors criptográficos.

Comandos de lectura: git status/rev-parse, rg/Get-Content de fuente/tests/SPEC/ADR, wsl --list --verbose, Get-CimInstance Win32_Service, Get-Item VersionInfo y Test-Path de evidencia histórica. Ubuntu/docker-desktop WSL2 detenidos; no se arrancaron. Servicio existente Running/Auto/LocalService solo observado. Lectura VersionInfo no identifica paquete instalado con exactitud.

Control de alcance: SHA256 de 582 archivos tracked src/tests/extension/scripts, cero cambios entre lecturas. Git muestra únicamente documentación modificada/nueva por esta entrega. Sin servicios instalados, privilegios, payloads personales, bloqueos ni conexiones a equipos del hermano.

Seguridad/recuperación: Sync requiere rol nuevo, consentimiento por usuario, recovery listo y tickets. No simular RecoverySaved. HPKE C#/JCS/Android definitivo pendientes de adopción verificable; no primitives caseras. Login, kit E2EE y recovery de bloqueo separados.

UX: flujos escritos y alcance Apple simulado; no nuevas pantallas implementadas. ZDR solo categorías aplicables, no toda la cuenta/metadatos. Hardware reportado: Windows hermano, S24 hermano, iPhone15 Pro Max propio; Mac no reportado.

Siguiente tarea: resolver paquetes exactos y ejecutar AND-0/LIN-0/fixtures crypto cuando el propietario autorice prototipos de código aislados. La restricción vigente no código impide cerrar esos ítems mediante implementación ahora; detalle de cobertura en docs/cloud-phase0/README.md.

## Revisión adicional de bootstrap

Turno siguiente: revalidado estado Git/documentos; no llegó autorización para prototipos. Se detectó que el contrato de lectura de pairing pendiente declaraba acceso exclusivo del solicitante sin prueba explícita de su clave. Añadidos X-FG-Pending-Proof, requestId y timestamp y binding de challenge/access token; ADR0017 y semántica actualizados. Comprobación del JSON embebido: 31 operaciones, 53 schemas, 383 referencias resueltas, headers pendientes obligatorios, cero errores. Esta es verificación estructural/documental, no prueba de crypto ni de backend.

Auditoría de cierre: las pruebas de viabilidad Android/Linux y paquetes interoperables continúan pendientes. La pregunta de autorización sigue sin respuesta; el objetivo permanece activo y no se redujo a documentación. No se creó ningún prototipo ni se modificó código.
