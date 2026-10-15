# Preparación Fase 3: adaptador HTTP de ajustes

## Plan y alcance

Adaptar el contrato actual /v1/documents sin modificar servidor desplegado, credenciales ni app instalada. Separar sender nativo de identidad (bearer privado, token digest exacto, HTTPS/origin y no redirects) del constructor de prueba de dispositivo y parser de respuestas. Probar contratos, errores y límites localmente.

## Implementación

SettingsDocumentHttpTransport fija HTTPS origin exacto, cuenta/generación/entidad y dispositivo. Comprueba AccountBinding antes del envío y otra vez dentro del callback de identidad tras refresh. Cada intento HTTP usa request ID/timestamp frescos y prueba FG-REQUEST-1 sobre método/ruta/account/generation/device, digest del bearer realmente adjuntado y digest del body JCS. El OperationId durable pertenece a la outbox; no se reutiliza como ID de prueba de posesión.

POST verifica routing y firma del sobre saliente; reconoce solo DocumentRevisionConflict como conflicto CAS. Membership/rekey son errores distintos. Una respuesta Created exige State Stored, entidad/revisión y ETag fuerte correctos, pero el worker exige además read-back comprometido. GET limita body, tipo JSON, esquema, duplicados, routing, revisión y ETag; la firma/AEAD/historia se verifican en el worker. 401/403/429/5xx/redirects se clasifican sin mostrar cuerpos ni secretos.

## Límite descubierto del contrato actual

El 404 DocumentUnavailable puede ocultar falta de acceso o ausencia: se trata como AmbiguousMissing y NO como baseline vacía. Inicializar el primer documento global de forma segura requiere acción explícita o mejorar el contrato local de ausencia autorizada; no se implementó ni desplegó esa mejora. Tampoco hay cursor, backoff ni sustitución automática de credenciales/membresía/época.

## Pruebas y honestidad de evidencia

Los ensayos de esta etapa usan un sender capturador con bearer/digest sintéticos y respuestas controladas; prueban bytes HTTP, firmas reales, route/ETag/schema y clasificación, no sockets/TLS reales, OIDC ni ledger PostgreSQL. NativeSettingsRequestSender está enlazado al método existente de IdentitySession, pero no se ejecutó contra la cuenta del propietario. No reemplaza la aceptación HTTP/OIDC/DB o física pendiente.

El módulo nuevo apunta a Identity Native; ninguna referencia apunta desde componentes distribuidos hacia sync. No se requiere instalador. Fase 2 y Fase 3 siguen abiertas. Registrar resultado final abajo.

## Ensayo HTTPS real añadido

SettingsHttpsLoopbackTests inicia Kestrel en 127.0.0.1 con puerto dinámico y certificado de laboratorio cuyo pin SHA256 acepta únicamente el HttpClient de la prueba. No modifica certificate stores ni políticas del sistema. Verifica solicitudes HTTP reales, prueba de posesión Ed25519 y cuerpo, sesión sintética, CAS y ETag; almacena solo el sobre opaco en memoria. Una publicación inicial explícita precede al tick del worker, precisamente porque 404 no prueba ausencia autorizada para autoinicialización.

El worker leyó revisión1, preparó/persistió/publicó revisión2 y confirmó su lectura cifrada; la publicación duplicada base0 fue rechazada como conflicto. No hay OIDC real, historia de membresía real o PostgreSQL en este fixture; no es aceptación del backend desplegado ni de dos equipos físicos.

El primer ensayo falló en TLS. Logs de Kestrel mostraron explícitamente AuthenticationException por falta de soporte de ephemeral keys de Windows/SChannel. Se importó el PFX generado solo en memoria a un contenedor temporal UserKeySet, sin PersistKeySet ni instalación en store, con limpieza al Dispose; el buffer PFX se borra. El ensayo HTTPS repetido pasó. Se retiró el logging temporal de diagnóstico.

Resultado final:58/58 pruebas locales PASS tras formato, incluido HTTPS/TLS/socket real con pin/caso CAS y worker; formato Windows/Tests verificado, diff-check exit0 y AST actualizado. Comprobado que ningun runtime distribuido referencia sync. No cambio de staging, instalacion ni credenciales reales. Los limites del adaptador son de parsing/lectura; IdentitySession existente usa ResponseContentRead antes de entregar la respuesta, por lo que falta revisar/añadir lectura streaming acotada nativa antes de habilitar runtime. Tampoco se declara validado ese limite de memoria upstream. Fase2 y Fase3 siguen abiertas.
