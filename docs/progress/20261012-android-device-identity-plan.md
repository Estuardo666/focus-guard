# Plan — identidad local Android compatible con FocusGuard

## Objetivo de este corte

Extender el probe Android aislado para crear y restaurar una identidad local de instalación con UUID canónico, firma Ed25519 y clave de cifrado X25519, compatibles con los contratos `DeviceIdentity` de Windows. La identidad y sus semillas de ensayo se guardarán dentro del vault AES-GCM ligado al binding de cuenta. El APK seguirá sin permiso de red, cuenta real, bloqueo ni sincronización.

## Alcance y aceptación

- Usar libsodium 1.0.22 a través del JNA ya empacado; no implementar primitivas criptográficas.
- Mantener separadas las semillas Ed25519/X25519; derivar las claves públicas de la misma forma que el contrato Windows.
- Calcular la huella con JCS, SHA-256, base64url sin padding y dominio `FG-DEVICE-1` + NUL.
- Firmar/verificar payloads acotados con separación de dominio compatible con `DeviceIdentity.Sign`.
- Persistir sólo un registro binario fijo y versionado dentro de `AndroidDeviceVault`; no escribir semillas en archivos sin cifrar, strings JSON, logs ni pantalla.
- Probar en AVD: creación, recarga tras reiniciar el proceso, claves/huella coherentes, firma válida y rechazo de mensaje alterado, separación entre bindings, ausencia de semilla en el sobre en disco.
- Comparar un vector determinista Android/C#; guardar y mostrar únicamente los valores públicos y el resultado, nunca las semillas.

## Seguridad, UX y recuperación

- El Keystore Android cifra las semillas; el vault continúa usando AAD de cuenta, `AtomicFile` y `noBackupFilesDir`. El registro evita codificar semillas privadas en strings base64 inmutables. El dispositivo AVD reporta Keystore software, por lo que esto no demuestra protección respaldada por hardware.
- Una pérdida/invalidez de la clave Keystore deja la identidad ilegible. No crear una identidad sustitutiva de forma silenciosa ni habilitar fallback en claro; el probe debe reportar el fallo.
- El UUID representa esta instalación y la identidad local no concede membresía ni capacidad remota. Pairing/aprobación, recuperación, membresía, API y aplicación Android productiva quedan fuera de este corte.
- La UI del probe expondrá estados y una huella pública acortada; nunca claves privadas, semillas o payloads de cuenta.

## Secuencia

1. Implementar wrapper JNA Android reutilizable e identidad con creación/importación, derivación, JCS/huella, firma y verificación.
2. Añadir carga/creación/serialización estricta usando un binding de probe separado del ensayo del vault genérico.
3. Extender la Activity con resultados seguros y pruebas de manipulación, binding y persistencia.
4. Generar un vector fijo en C# y compararlo desde Android; construir APK, instalar/reiniciar el AVD existente y comprobar la UI y el manifest.
5. Actualizar este checkpoint, `TASKS.md` y graphify; registrar cualquier gate físico/producción que siga pendiente.
