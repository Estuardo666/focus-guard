# Fase 3 — integración local de consentimiento y ciclo automático

## Alcance de este checkpoint

Preparar un coordinador independiente que componga el consentimiento local persistido, la sesión cancelable, la compuerta durable de reintento y el scheduler ya existentes. El coordinador no se conectará al Host ni activará sincronización en la aplicación distribuida.

## Plan y aceptación

1. Exigir una llamada explícita del propietario para iniciar el loop; antes de iniciar solo se reanuda un consentimiento guardado previamente.
2. Guardar el opt-in de manera durable antes de habilitar ticks; exigir confirmación deliberada al activarlo.
3. Al desactivar, cortar el tick en curso, persistir la revocación y despertar al scheduler; conservar el borrador local.
4. Mantener el backoff/checkpoint durable existente y dejar los cambios de ajustes como señales agrupables.
5. Cubrir consentimiento apagado, inicio con consentimiento previo, alta explícita, revocación en vuelo, reinicio del loop y cancelación/disposición con pruebas locales.

## Riesgos y límites

El coordinador solo puede garantizar la revocación dentro del proceso cuando los adaptadores respetan `CancellationToken`; el worker existente vuelve a comprobar el consentimiento persistente antes de operaciones de red y escritura. No se implementan push, servicio en segundo plano, captura de cambios del Host, UI de consentimiento ni conexión a servicios cloud reales. Una cancelación no borra el outbox ni los ajustes locales.

## Estado

En curso; la aceptación final se registrará tras pruebas, formato y actualización del grafo.
