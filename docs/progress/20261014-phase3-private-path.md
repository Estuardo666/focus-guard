# Fase 3 — directorio local por scope con ACL protegida

## Resultado

`SettingsSyncLocalStorage.PrepareCurrentUserDirectory` deriva su raíz de `%LocalAppData%\FocusGuard\Sync`. El overload de pruebas acepta una raíz aislada. En ambos casos prepara la raíz y una subcarpeta determinista con SHA-256 del issuer, accountId, generationId y deviceId; el path no revela esos valores.

Antes de devolver la ruta, valida que root y subcarpeta sean directorios normales, que el propietario sea el usuario Windows actual y reemplaza la DACL por permisos explícitos de FullControl para usuario actual, SYSTEM y Administrators. Verifica la ACL final. El store DPAPI escribe y vuelve a abrir el checkpoint bajo el scope correcto.

## Validación

- Scope idéntico produce la misma ruta; cambiar issuer, cuenta, generación o dispositivo produce otra.
- Prueba Windows lee propietario/SIDs/ACL reales y confirma que no haya identidades fuera de usuario actual, SYSTEM y Administrators.
- Ningún issuer o ID aparece en el path.
- `SettingsRetryCheckpointStore` guarda y vuelve a leer con DPAPI `CurrentUser` dentro de esa carpeta.
- Suite completa `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore`: **135/135 PASS**.
- La carpeta de prueba temporal se elimina al terminar; la carpeta real `%LocalAppData%\FocusGuard\Sync` no se creó ni se modificó.

## Límites

La utilidad no se llama desde Host ni migra archivos existentes. Antes de integrar, decidir dónde mantener consentimiento/outbox, cómo migrar o volver atrás y cómo manejar permisos denegados sin borrar datos. La prueba cubre ACL locales Windows, no sincronización en dos dispositivos ni servicio cloud.
