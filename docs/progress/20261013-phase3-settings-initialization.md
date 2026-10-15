# Preparación Fase3: inicialización explícita CAS

Plan: resolver primer documento sin interpretar 404 como permiso para resetear; confirmación deliberada, intención cifrada durable base0, CAS y lectura posterior verificada. Sin cuenta/staging/app reales.

SettingsDocumentInitializer solo opera tras ownerConfirmed, scope exacto y baseline local ausente o intención inicial compatible ya pendiente. Guarda ciphertext/operation antes de solicitar; 404 ambiguo permite únicamente intentar ese create firmado/CAS0 ya confirmado, nunca afirmar ausencia ni habilitar polling. El servidor sigue validando OIDC/membresía/WriteSettings. Un documento existente se lee/verifica/reconcilia sin sobrescribirlo; confirmación de éxito exige eco exacto, no receipt. Estado reconciliado en conflicto no se publica automáticamente.

Create explícito de defaults no se trata como no-op: el draft inicial se construye expresamente, con consentimiento automático aún false. Reintento tras respuesta perdida conserva bytes y recupera desde readback sin segundo POST. No se puede reinicializar estado local confirmado, borrar/reemplazar vault ni conceder sync consent.

Fixture HTTPS real de laboratorio inicia CAS0, simula pérdida de respuesta después de commit, recupera por GET y luego prueba worker/reset. TLS/firmas/DPAPI reales, identidad/membresía sintéticas y almacenamiento en memoria: no prueba backend PostgreSQL/OIDC desplegado ni otro Windows. UI debe proporcionar confirmación nativa; el booleano no es permiso proveniente de contenido remoto.113 pruebas PASS iniciales con nuevas aserciones; formato/cierre se registran abajo. Fase2/Fase3 siguen abiertas; runtime no conectado.
