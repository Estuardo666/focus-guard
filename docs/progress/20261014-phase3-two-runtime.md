# Fase 3 — prueba de convergencia con dos runtimes locales

## Resultado

La prueba inicia dos `SettingsAutomaticSyncRuntime` independientes. Cada uno usa su propio scope `issuer/account/generation/device`, carpeta protegida, outbox DPAPI, consentimiento, checkpoint retry y scheduler. Los dispositivos tienen claves de autor diferentes y comparten el mismo documento cifrado/account content key.

Un ledger en memoria verifica la firma del autor, limita key epoch y membership revision al valor de la membresía sintética de la prueba, impone CAS de revisión y almacena solo los bytes del sobre. Secuencia: primer cliente cambia tema; el segundo recibe actualización al simular reconexión; segundo cambia idioma; el primero converge al recibir su siguiente aviso. Ambos mantienen la duración predeterminada y terminan sin outbox pendiente. El wire almacenado no expone claves de campos ni valores en claro.

## Validación

- La prueba dirigida pasó **5 veces consecutivas** sin fallos.
- Suite `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore`: **136/136 PASS**.
- `dotnet format` en modo verificación para el test nuevo: PASS.

## Limitaciones

La resolución de autores usa claves públicas sintéticas preseleccionadas, no verifica una cadena de membresía real. El ledger es local, no HTTP/TLS, OIDC, Neon ni API staging. DPAPI de ambos scopes lo protege el mismo usuario Windows; no demuestra otra PC, instalación física ni transferencia entre propietarios. La prueba no conecta ni activa el Host. Fase 2 continúa abierta para la verificación física propietario→hermano.
