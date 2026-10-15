# CLOUD-0 — Flujos UX escritos

Fecha: 2026-10-11. Diseño, no pantallas implementadas. Mantener tokens y carácter tranquilo de UX_SPEC y docs/design/FOCUSGUARD_DESIGN.md. No convertir cuenta cloud en requisito del bloqueo Windows local.

## Cuenta

Bienvenida → Continuar localmente / Conectar mis dispositivos. Mostrar antes de login qué se sincroniza, qué sigue local y qué significa Apple simulado.

Login/registro en dominio de identidad: correo (usuario), contraseña, mostrar/ocultar, recuperar acceso, verificación de correo y MFA. App nativa abre navegador externo; cancelar vuelve a estado local sin alterar sesión. Login exitoso no equivale a equipo vinculado.

Estados: Idle, Redirecting, AwaitingCallback, Verifying, SignedInUnlinked, SignedInLinked, Error. Callback perdido/duplicado no reintenta login con password ni crea devices duplicados. Mostrar error genérico; no anunciar existencia de correo en reset.

Perfil personal: nombre visible, correo/verificación, seguridad de identidad. Idioma/zona/theme via documentos cifrados. Diferenciar «Perfil de cuenta» de «Perfil de bloqueo». Editar correo requiere identity flow, no PATCH libre en API. Desconectar cuenta no cancela Strict aplicado; mostrar fin y recovery antes de confirmar.

## Vinculación

Primer equipo: login → alta sync → generar kit E2EE → mostrar/exportar localmente → confirmar guardado → prueba de sync no bloqueante → configurar permisos normales/Strict aparte.

Segundo equipo: login → QR/código + fingerprint → aprobador ve plataforma/huella → comparar en ambos → aprobar → esperar entrega cifrada → confirmar posesión/descifrado → configurar consentimiento local. Pending no aparece como listo.

QR expira en 5 min; regenerar invalida solicitud anterior. Si se cancela, no transmitir secrets ni dejar solicitud active. Sin confiable: kit; sin kit: generación nueva con aviso de pérdida, no sustitución silenciosa de anclas.

Las pruebas con Windows/S24 del hermano necesitan consentimiento de su dueño; no convertir su sesión personal en receptor de la cuenta del propietario sin revisión local deliberada.

## Settings

Cuenta / Perfil personal / Dispositivos / Sync global / Seguridad / Privacidad.
Tarjeta de device: nombre cifrado mostrado localmente, plataforma, versión, lastSeen, alcance, canales, included y estado de consentimiento. Online y Protected son hechos distintos.

Sync: modo claro/oscuro, idioma/duración, campo local/global y «Guardado aquí / Pendiente / Sincronizado / Conflicto». La paleta de color y el estilo del orbe permanecen locales. Merge seguro de campos diferentes; conflicto mismo campo exige comparar. No actualizar política activa.

Seguridad: sesiones, MFA, revocar dispositivos y kit. Pausar recepción afecta nuevas intenciones; indicar que sesión actual conserva fin. Revocar deniega sync y rota epoch, sin prometer borrar copias offline.

Privacidad: matriz de datos y retención, exportar localmente y borrar cuenta. Texto: «No recopilamos historial de navegación ni enviamos reflexiones. Perfiles y ajustes se sincronizan cifrados. Conservamos los datos mínimos necesarios para tu cuenta y sincronización». No etiqueta ZDR total.

## Bloqueo normal

Focus → Este equipo / Mis dispositivos → perfil + duración → revisar destinos y hora exacta → iniciar → resultados individuales. Offline Pending, ACK obsoleto Unknown. Al finalizar normal, mostrar parada pendiente en destinos offline; no sustituir sesión local ocupada.

Ejemplo: «Windows: apps aplicadas, sitios pendientes; Samsung: sin permiso; iPhone: simulación». Agregado con desglose, no «todo bloqueado» si solo llegó un envío.

## Strict remoto

Preparación local una vez: explicar resistencia/fin, iniciadores/perfiles permitidos, trabajo sin guardar, provisionar recovery y comprobar helper offline. Requiere dueño presente; no modal de permisos automatizada.

En origen: elegir Strict + Mis dispositivos, mostrar tiempo/fin, reauth/MFA, preparar. Pantalla Ready muestra cada ticket y TTL, alcance real/limitado/simulado, exclusiones y consecuencias. Confirmar nombre exacto del perfil + trabajo sin guardar. Cambiar perfil/duración/destinos invalida review/tickets.

Si falta un destino: «No podemos preparar todos; no se ha aplicado ningún bloqueo». Esperar/cancelar/cambiar conjunto; cambiar conjunto cambia también la etiqueta de todos.

Después commit: «Aplicado en 2, pendiente en 1, 2 simulaciones». No botón Finalizar global para Strict. Mostrar recovery local con demora, fin absoluto, pérdida de red. Recovery de un equipo no termina otros. Caducar ticket no apaga un Strict ya aplicado.

## Apple

iPhone 15 Pro Max: companion Safari/PWA, sesiones reales cloud y pantalla simulada. macOS mismo alcance, no equipo confirmado disponible.
«Strict simulado: esta app refleja la sesión. No impide cerrar la app ni usar otras aplicaciones».
Al volver a primer plano → consultar snapshot; no mostrar Applied por animación ni prometer actualización mientras cerrada.

## Accesibilidad y errores

Keyboard/foco visible, labels/error asociados, contraste sin color único, lectores de pantalla y targets táctiles. Estado de sync no anuncia cada heartbeat. ReduceMotion respeta preferencia OS; UI no controla fases por animaciones.

Errores tipados traducibles: ConsentRequired → habilitar localmente; RecoveryNotReady → provisionar recovery local; ClockUncertain → verificar reloj; PreparationExpired → revisar de nuevo; PolicyUnsupported → mostrar canales/reglas incompatibles. Nunca exponer stacks/keys/rules sensibles en mensajes de soporte.

## Casos de aceptación escritos

Login cancelado conserva modo local; QR duplicado no registra dos devices; reset password no recupera claves; conflicto settings no sobrescribe; normal/Strict no se mezclan; recuperación perdida no activa Strict; Apple nunca cuenta como protección real; borrar cuenta conserva fin/recovery de ejecución vigente.

