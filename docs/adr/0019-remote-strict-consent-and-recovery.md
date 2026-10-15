# ADR 0019 — Strict remoto, consentimiento y recuperación

Fecha: 2026-10-11. Estado: decisión de diseño para desarrollo futuro; no habilita Strict remoto instalado.
Complementa SPEC Strict/Recovery y ADR 0011. El contrato local actual continúa generando/confirmando recovery por sesión; este documento define la ampliación remota sin fingir esas confirmaciones.

## Decisión

Strict remoto en cinco clientes. Windows/Linux: real solo tras gates; Android: resistencia limitada declarada según OS/permisos; macOS/iPhone: simulación explícita. Origen autorizado en cualquiera de ellos, con login reciente y confirmación de consecuencias.

Recepción remota Strict desactivada por defecto. Provisión local con propietario presente: recovery credential de alta entropía, mostrar una vez, guardar explícitamente y hash salado protegido del lado motor. Reutilización acotada para recepción remota mientras la provisión siga vigente; rotar solo sin sesión Strict activa. Mantener mínimo 15 min de demora Windows, elevación y Safe Mode/offline existentes. No acortar recovery desde cloud.

El registro local vincula credentialId, consentimiento y scope, nunca expone hash/secreto al cloud. Tickets solo indican recoveryReady. Reconocimiento local de futuras consecuencias y trabajo sin guardar se registra en consentimiento; origen vuelve a mostrar advertencia en cada revisión.

Sin provisión válida, preparar localmente código por sesión y confirmar guardado antes del ticket, o rechazar. No automatizar el checkbox local RecoverySaved ni copiar secreto entre equipos. Recovery cloud de login/clave E2EE es distinto de recovery de bloqueo.

## Protocolo

Preflight → intent firmado con ventana exacta y snapshot → Preparing (≤120 s) → tickets Ready por destinos congelados → autorización breve de operación + commit firmado → Applied/Degraded/Simulated por destino.

Ticket local y firma dispositivo vinculan generation/account/session/preparationId, intentDigest, snapshot lógico, compiledPolicyDigest, mappingRevision, consentRevision, membershipRevision, epoch, start/end y expiresAt. El servidor puede verificar firmas e IDs, pero no certificar enforcement.

Commit exige todos los destinos elegidos listos; si uno falta, no «en todos». Cambiar conjunto invalida intención y tickets. El destinatario valida commit, vigencia/revisiones y consentimiento nuevamente, luego persiste decisión y consume ticket una vez antes de activar. Un crash después de persistir se reconcilia con motor y ledger; jamás activar un nuevo ID por timeout.

No atomicidad distribuida prometida. Tras commit, algunos pueden fallar; mostrar parcial. No rollback mediante stop normal. Rechazar stop/skip/cambios de política/duración en Strict; cancelar solo preparación no comprometida.

Normal late-join no aplica a Strict. Adhesión después de ticket vencido exige nueva preparación y confirmación local, mismo fin global, contrato específico y validación de slot. El MVP remoto Strict puede rechazar adhesión tardía por completo y mantener destino pendiente/rejected sin ocultar alcance.

## Persistencia y recuperación

Strict aplicado conserva mismo ID/hash/end en reboot donde se haya probado. UI/Internet/logout no mantienen ni extienden autoridad. Integridad/autoridad local inválida → liberación segura/cuarentena según especificación. Recuperar un destino no para otros. Cuenta borrada no elimina recovery/snapshot local antes de terminar.

Clock rollback: elapsed monotónico en boot; reconciliación persisted time bajo pruebas de adversario. Si no se puede verificar integridad o acotar fin seguro, no crear duración nueva; recovery/fail-open documentados, sin bloqueo indefinido.

## Cambios futuros obligatorios

1. SPEC: ámbito estricto remoto normal timer, provisión local reutilizable, preconsentimiento, autoridad/limites; sin modificar garantías de local-only.
2. IPC: rol Sync protegido y mensajes remotos dedicados, autorización OS/motor y pruebas negativas por rol.
3. Store: credentialId/hash bajo ACL; tickets/commits/ledger integrity y tombstones locales.
4. UI: revisión y fin exacto, todos/parcial, Strict limitado Android y Strict simulado Apple.
5. Gates por OS: recovery offline, fin automático, reboot, corrupción, reloj, exclusiones y permisos.

## Aceptación

Preparar nunca bloquea. Recovery no listo impide ticket. No amplificar consentimiento por cambio de perfil/nuevo iniciador. Tickets modificados/expirados y pruebas de auth replay fallan. RecoverySaved del bridge local actual no se simula. Strict remoto no se anuncia hasta evidencia instalada propia, distinta de drills Strict locales históricos.

