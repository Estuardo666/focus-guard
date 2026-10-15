# Fase 3 preparación local: reintentos y parada segura

## Plan y alcance

Añadir una política caller-driven al worker existente: backoff exponencial/jitter acotado, respeto de Retry-After, espera monotónica, consentimiento y parada ante errores críticos. No crear timers ni iniciar actividad en la app/cloud. Fase 2 sigue abierta por la otra PC.

## Implementación

SettingsRetryController serializa intentos. Espera un intervalo tras éxito/pending/conflicto y usa backoff de 5s exponencial con jitter, tope local 5min, para errores de disponibilidad/throttle y fallos transitorios HTTP clasificados. Retry-After superior nunca se acorta, incluso si supera el tope local. Los cambios de reloj de pared no aceleran la espera dentro de esta instancia porque usa GetTimestamp/GetElapsedTime.

Autenticación, permisos, membresía, rekey, protocolo, ausencia ambigua, TLS y errores de integridad/almacenamiento pasan a NeedsAttention; ningún poll los reanuda. ResumeAfterRecoveryAsync es una acción deliberada posterior a recuperación/reautenticación y no altera los datos de la outbox. Cancelar el token del caller propaga cancelación, no la convierte en reintento. Consentimiento desactivado evita Tick.

## Evidencia y límites

Tests de política con reloj virtual: Retry-After, salto de reloj, crecimiento/tope, no solicitudes durante cooldown, errores críticos persistentes, consentimiento, cancelación, TLS y recuperación tras éxito. No son pruebas de rendimiento/disponibilidad cloud. La suite conserva el fixture HTTPS local y crypto/DPAPI reales de los tramos anteriores.

No hay scheduler automático, persistencia de cooldown entre procesos/reboots, integración UI, cursor ni membresía real; esas tareas siguen pendientes. Una nueva instancia reinicia el estado de política: no debe declararse que Retry-After sobrevive al reinicio. Antes de activar runtime también permanece pendiente el límite streaming upstream del sender nativo documentado en el checkpoint HTTP.

No se modificó un componente distribuido ni se requiere instalador. Registrar formato/tests/diff/AST finales abajo.

Resultado final:70/70 pruebas locales PASS; formato Windows/Tests verificado; diff-check exit0 con avisos LF/CRLF preexistentes; AST actualizado. Comprobada ausencia de referencias desde componentes distribuidos. No se cambio cloud, credenciales ni app instalada; Fase2 y Fase3 siguen abiertas.
