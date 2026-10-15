# Preparación Fase3: coordinador de cola y preferencias locales

Plan: conectar outbox DPAPI y proyección durable mediante operaciones locales explícitas; persistir intención antes de preferencias y recuperar un fallo sin perder borrador. Probar solo en directorios temporales, no datos reales ni runtime. Fase2 sigue abierta por otro Windows.

LocalSettingsCoordinator.EditAsync valida ajustes/overrides, persiste borrador en outbox antes de llamar a DesktopSettingsSyncStore, y devuelve AppliedResult solo tras completar reemplazo. ReapplyAsync recupera intención persistida con otra instancia después de fallar/escritor corrupto. Sin baseline inicializada se rechaza; no crea una implicitamente, no habilita consentimiento ni llama a red/servicio.

Tests reales DPAPI/filesystem comprueban edición durable/reaplicación, conservación enforcement, fallo de preferencias que conserva borrador y archivo corrupto, reparación explícita del fixture y recuperación, rechazo de baseline ausente. No demuestra atomicidad de dos archivos: tras fallar preferencias la outbox puede estar adelantada; recuperación explícita completa la proyección. No usar Pending como prueba de guardado de preferencias.

Limitaciones antes de runtime: serialización del coordinador solo por instancia; otros coordinadores/workers/escritores legacy requieren una operación conjunta o versión/check bajo coordinación compartida para evitar proyección antigua. El store de preferencias conserva la carrera con escritor legacy documentada anteriormente. No se integra Host/UI, OIDC/membresía real, cursor o scheduler. No se cambia componente distribuido/instalador ni cloud.98 pruebas PASS iniciales; formato y repetición registrados al cierre.

Formato aplicado solo a modulos sync; suite posterior y diff-check forman parte del comando de cierre. No datos reales modificados. Grafo anterior confirmado completo; actualizacion de este tramo requiere esperar handle26739.

Coordinacion mejorada: UpdateAndProjectAsync y ProjectCurrentAsync mantienen lease OS de outbox durante escritura de preferencias. Dos coordinadores sobre mismo vault ya no proyectan estado antiguo frente a cambios cooperantes. Orden de locks outbox->preferences; callback no puede reentrar al vault. Prueba concurrencia dos instancias deja preferencias iguales al borrador final durable.99 pruebas PASS. Persisten limites frente al escritor legacy, rutas de outbox distintas sobre mismo archivo y crash entre los dos commits; no habilitar runtime aun. Grafo anterior completo exit0.

Edicion parcial: EditSelectedAsync toma campos no seleccionados de estado fresco bajo el lease outbox; evita perder idioma cuando otra instancia cambia tema y viceversa. Prueba dos instancias confirma combinacion y archivo final. Seleccion vacia/flags desconocidos rechazados antes de escribir.101 pruebas PASS; trabajo local sin Host/cloud. EditAsync sigue siendo reemplazo deliberado completo, no usarlo para patches de formulario obsoleto.
