# Plan — despertar sync al recuperar conectividad Windows

El scheduler expone `NotifyConnectivityRestored`, pero la aplicación Host no enviaba una señal de red. El intervalo de reintento eventual funciona; se añadirá una notificación del SO para despertar la sync después de recuperar conectividad sin saltar un backoff durable.

1. En Host, escuchar availability/address events con lifetime limitado a la ventana, señalar solo cuando Windows reporte disponibilidad y desuscribir al cerrar/dispose.
2. En `HostAccountService`, propagar la señal solo si el consentimiento permanece activo; ignorar sesiones ausentes/deshechas.
3. Probar que la señal despierta una poll idle y que revocar consentimiento impide trabajo adicional; conservar la política de `Retry-After`.
4. Correr Host/Sync/UI, formato y grafo; regenerar y validar Setup standalone por cambio de Host. No instalar, desplegar ni llamar staging.
