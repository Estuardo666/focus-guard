# Fase 3 — composición de sesión de ajustes en Windows

## Resultado

Se preparó `HostSettingsSyncSession`, una composición propiedad del Host para el futuro runtime de ajustes globales. La construcción valida que el transporte use la misma cuenta del vault, exige una cadena completa de membresía anclada al digest local, y requiere que este dispositivo conserve `Read` y `WriteSettings`. El contexto usa un identificador estable dedicado, distinto del documento de transferencia manual.

La sesión reúne la criptografía, el transporte autenticado, outbox y consentimiento DPAPI, checkpoint de reintento, proyección local, inicializador de baseline, edición, resolución de conflictos y runtime. Conserva cargado el vault y su material hasta cerrar la sesión. La ruta local se calcula sin I/O durante la construcción; sus directorios con ACL protegida se crean solo después del opt-in explícito.

`EnableAsync(false)` se rechaza antes de crear directorios, escribir preferencias o iniciar solicitudes. Con confirmación, la sesión crea un archivo local solo si falta; si ya hay contenido —incluido JSON corrupto— no lo reemplaza y la lectura estricta decide si puede continuar. El baseline cifrado se inicializa o se recupera reutilizando exactamente la operación/ciphertext de creación pendiente; el consentimiento persistente se guarda solo después de completar ese paso. Una sesión guardada con consentimiento puede correr únicamente cuando el Host invoca `RunAsync`.

## Seguridad y límites

- La cuenta del sender, la cuenta/vault local y el scope DPAPI deben coincidir.
- La cadena completa debe cubrir desde revisión 1 hasta la revisión confiable del vault; autores históricos se resuelven en su propia revisión.
- Solo se extraen los tres campos globales permitidos: tema, idioma y duración. Reglas, perfiles, permisos, historial y sesiones quedan locales.
- La composición no se conectó todavía a `HostAccountService`, acciones IPC/UI ni al ciclo de inicio del producto. No se activó en el equipo, no se llamó al staging y no se instaló el candidato.
- No reemplaza la aceptación física de Fase 2: sigue pendiente confirmar en el segundo Windows la transferencia propietario→hermano, duración y conservación de campos locales.

## Archivos

- `src/FocusGuard.Host.Windows/Hosting/HostSettingsSyncSession.cs`
- `cloud/sync/Windows/SettingsSyncLocalStorage.cs`
- `src/FocusGuard.Host.Windows/FocusGuard.Host.Windows.csproj`
- `tests/FocusGuard.Host.Windows.Tests/HostSettingsSyncSessionTests.cs`
- `tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj`
- `cloud/sync/Tests/DesktopSettingsSyncStoreTests.cs` (aserción de excepción acepta el subtipo `JsonReaderException`)

## Pruebas

- Host Windows: 224/224 aprobadas, incluida construcción inerte, opt-in rechazado sin efectos, creación de baseline cifrado con opt-in y rechazo de sender con otra cuenta.
- Sync Windows: 139/139 aprobadas.
- `scripts/test-installer.ps1`: aprobada con los metadatos `.163`.
- `FocusGuard-0.1.163-Setup.exe --package-self-test`: exit code 0.
- Setup `.163`: 371.512.232 bytes; SHA-256 `5BD1D831CBC55E85095AD38D62A4C06D0F7D0A620E8AA4F09069BFE36BBFAEC2`.
- ZIP `.163`: 552.312.531 bytes; SHA-256 `0E384FE0DE0D20491E6CA2EE6460189CAB77464EF34C4A5963ED0AF2D9DCAF62`.
- Host publicado contiene `FocusGuard.Sync.Windows.dll` (123.392 bytes). `.163` no fue instalado ni desplegado. `.162` quedó supersedido y su retirada también sigue sujeta al bloqueo de limpieza ya registrado.

## Próximo tramo

Conectar la fuente de membresía verificada y vault de `HostAccountService` a esta composición; añadir una acción de consentimiento explícito en el Host/UI, enrutar guardados locales por el coordinator, y definir el ciclo de inicio/recuperación. Mantener Fase 2 abierta hasta la prueba física en el segundo PC.
