# ADR 0016 — Autoridad cloud/local y entrega interdispositivo

Fecha: 2026-10-11. Estado: decisión de diseño de CLOUD-0; no implementada.
Origen: petición de cuenta cloud y bloqueo global, incluido Strict, en PLAN-CLOUD-MULTIDISPOSITIVO.md.
No modifica comportamiento instalado. Complementa ADR 0002, 0009 y 0011; las garantías locales actuales prevalecen hasta implementar contratos nuevos.

## Decisión

Cloud coordina identidad, destinatarios, orden, documentos cifrados y ACK; cada motor conserva autoridad de política, salud, recuperación y fin. La caída de Internet/identidad no prolonga ni invalida automáticamente una sesión local aplicada. La caída de autoridad local conserva fail-open y exclusiones existentes.

API y worker: monolito modular ASP.NET Core, PostgreSQL, outbox transaccional y una región inicialmente. SignalR avisa cursor; consultar snapshot antes de ejecutar. Sin puertos entrantes del servicio local.

Una intención global por generación de cuenta, con exclusión transaccional. Preparing ocupa una reserva máximo 120 s. Cancel/commit hacen CAS; solo uno gana. Commit no significa Applied. Fin absoluto libera exclusión aunque no haya ACK.

Entrega al menos una vez; efecto local idempotente, persistido por generationId/sessionId/commandId. Nuevo requestId para reintentar transporte, mismo commandId para reintentar efecto. Repetir idempotencyKey con contenido diferente devuelve Conflict.

## Tiempo y reboot

Normal cloud: 60–86400 s de ventana revisada. Strict remoto: 60–14400 s. UTC y fin inmutable; preparación/transporte descuentan tiempo. La activación tardía normal puede durar menos de 60 s: se considera adhesión a ventana ya revisada, nunca un temporizador manual nuevo. Requiere al menos 1 s restante y contrato IPC remoto explícito; el contrato manual actual no cambia.

Aplicación normal tardía predeterminada 600 s desde startAtUtc; después, confirmación local. Strict requiere ticket vigente al commit y al consumo, máximo 120 s; ningún ticket vencido se reactiva por un recibo antiguo.

Reloj local: anchor UTC contrastado con servidor y elapsed monotónico. Umbral de incertidumbre inicial 5 s; rechazar nuevos inicios remotos si no se acota. Nunca asumir que el reloj del servidor es una raíz criptográfica; usar detección de cambios y límites locales. Tras reboot, restauración Strict solo con persistencia íntegra probada y fin original; normal se registra Interrupted y no se restaura.

Persistir interrupción/tombstone antes de ACK. Cancelar preparación no cancela Strict. Parada normal terminal rechaza inicios tardíos. Una sesión nueva no reemplaza una local ocupada; conflicto visible.

Rechazar relojes/contexto de salud desconocidos o no finitos, y tiempo restante que supere la duración máxima consentida localmente. No truncar de manera invisible el fin revisado: pedir nueva revisión/conjunto. Reserva y deadline son finitos, inmutables y caducan a más tardar con el fin de sesión; llamadas ready/commit tardías no cambian estados terminales.

## Resultados por destino

Estado de entrega, tipo de modo y alcance separados. Applied requiere ACK del motor/canales solicitados; AppliedDegraded declara faltantes, Simulated solo Apple. Offline → Pending/Unknown, conservando fecha de último reporte. ACK firmado es declaración del cliente, no atestación hardware.

Los canales apps/browser mantienen ADR 0009. No liberar un canal sano por fallo de otro. Un Strict puede degradarse por pérdida de capacidad, nunca convertirse automáticamente en Normal.

## Alternativas

Backend gestionado: menos operación, más dependencia. P2P: disponibilidad móvil/NAT dificultan entrega durable. Monolito propio elegido por stack y control; no microservicios antes de medir necesidad.

## Aceptación futura

Dos Windows, duplicados, ACK perdido, stop-before-start, fin sin red, reboot normal/Strict, preparación abandonada, cancel/commit concurrentes y canales parciales. Toda activación verifica generación, membresía y consentimiento vigentes.

Implementación depende de [contratos](../cloud-phase0/CONTRACTS.md) y [Strict](0019-remote-strict-consent-and-recovery.md).

