# Preparación Fase3: proyección de preferencias Windows

Plan: enlazar el esquema permitido con DesktopSettings real mediante un adaptador puro; demostrar conservación de toda configuración local no sincronizable. Sin escritura en preferencias reales, instalación ni despliegue; Fase2 mantiene aceptación pendiente con otro Windows.

DesktopSettingsProjection extrae únicamente tema/idioma/duración y aplica valores globales verificados tras overrides explícitos usando copia inmutable. Conserva todos los otros campos: enforcement, perfiles/presets, movimiento reducido, notificaciones, ventana y preferencias de sesión. No tiene acceso a servicio ni sesión activa; duración significa la preferencia de próxima sesión, no cambiar una sesión en curso. Valores locales inválidos se rechazan, no se publican defaults silenciosamente.

La referencia va del módulo de preparación Windows hacia Client; no modifica Client ni introduce referencia inversa desde Host/Service/API. No requiere instalador. Tests verifican igualdad completa del DesktopSettings salvo los tres campos permitidos, overrides separados y validación. No prueban UI, persistencia DesktopSettings, scheduler o convergencia física.

Suite local:90/90 PASS. Formato/diff se verifican al cerrar el tramo. La actualización AST anterior fue confirmada exit0 antes de iniciar este trabajo; una actualización posterior debe completar antes de declarar el grafo actual. No habilitar runtime con este adaptador aislado.

Cierre de validacion:90/90 PASS repetidas tras formato; whitespace Windows/Tests verificado y diff-check exit0. Actualizacion AST posterior en proceso; no afirmar completada sin consultar su handle. Fase2 y Fase3 abiertas; sin activar runtime.

Proyeccion de outbox añadida: ApplyOutbox usa el borrador pendiente antes que la confirmacion remota, para que un ACK anterior no revierta cambios locales nuevos. Regresiones prueban ACK tras editar idioma/duracion y conflicto que conserva valor local sin preparar envio.92/92 pruebas PASS, formato aplicado y diff-check exit0; graph previo confirmado exit0. Sin escritura DesktopSettings real ni UI/runtime conectado.

Persistencia local preparada DesktopSettingsSyncStore: explicit path, lectura fresca sin fallback, conserva campos JSON desconocidos, temporal/flush/reemplazo y retorno despues de commit. Error/cancelacion propagados, fixtures temporales sin tocar preferencias reales.94 pruebas PASS. Lease solo coordina instancias de este adaptador; escritor legacy no participa: comparacion previa detecta cambios anteriores al check pero queda carrera check/replace. No habilitar runtime hasta unificar escritores. Sin componentes distribuidos modificados.

Guardas de ambiguedad: rechazo recursivo de propiedades duplicadas/case variants antes de proyectar o escribir. Regresion verifica archivo original intacto.95/95 pruebas locales PASS, diff-check exit0. Grafo previo confirmado exit0; se solicita actualizacion para nueva regresion. Modulo no conectado, aun requiere coordinacion con escritores legacy.

Lectura de preferencias acotada antes de asignar memoria: FileStream comprueba longitud<=1MiB y lee exactamente bajo handle sin escritor concurrente. Tanto baseline como relectura previa al commit acotadas. Test archivo sobredimensionado confirma rechazo y archivo intacto/sin temporal.102 pruebas PASS, formato aplicado, diff-check exit0. No resuelve carrera legacy check/replace ni activa runtime.

Baseline local requerida: theme numerico, uiLanguage string y lastDurationMinutes entero deben existir antes de proyectar. Archivo incompleto/tipos equivocados rechazado sin escribir defaults.103 pruebas PASS, diff-check exit0 y formato aplicado. Datos reales no tocados; aun requiere coordinacion con escritor legacy. Grafo anterior confirmado exit0.
