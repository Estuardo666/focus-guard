# Android virtual de ensayo

Instalado con autorización del propietario. Imagen Android15 AOSP, API35, x86_64; nombre FocusGuard_Phase0_Android15 y serial emulator-5570. Emulador oficial37.3.3, 2CPU/2GB RAM, cámaras/audio desactivados y 2GB de datos configurados.

Ubicación: artifacts/cloud-phase0/android-vm. SDK y dispositivo separados de la aplicación Windows y de teléfonos físicos. Sin cuenta Google ni Play Store. WHPX comprobado por emulator -accel-check como operativo; no habilitar features Windows ni reiniciar equipo.

Si la unidad E: no tiene espacio para la partición virtual configurada, el launcher usa la copia local de `%LOCALAPPDATA%\FocusGuard\AndroidVM\avd` cuando ya existe; el AVD original permanece en `artifacts/cloud-phase0/android-vm/avd`. El SDK continúa aislado en los artefactos del proyecto.

## Abrir

Doble clic en Abrir-Android-VM.cmd dentro de esta carpeta, o desde el repositorio:

```powershell
./prototypes/cloud-phase0/android/android-vm.ps1 -Action Show
```

Show reinicia únicamente este AVD si estaba headless y abre su ventana. Esperar el primer arranque; no iniciar una segunda copia si se está iniciando.

## Estado / apagar

```powershell
./prototypes/cloud-phase0/android/android-vm.ps1 -Action Status
./prototypes/cloud-phase0/android/android-vm.ps1 -Action Stop
```

Los comandos verifican nombre/serial del emulador; no detienen el servidor ADB global ni controlan otros dispositivos. No borran el AVD ni sus datos. La VM no se inicia con Windows.

## Evidencia

Boot sys.boot_completed=1, Android release15/API35. APK app.focusguard.phase0 instalado y Activity ProbeActivity abierta (am start Status:ok). UI real confirma API35, modelo SDKx86_64 y permisos no concedidos inicialmente. No declara bloqueo normal/estricto validado. Screenshot y XML en artifacts/cloud-phase0/android-vm.

El primer avdmanager falló porque la imagen AOSP nueva usa un marcador de partición vacía; se configuró el AVD explícitamente con la imagen oficial verificada. Android arrancó realmente, no se reemplazó la imagen por un mock. Android CLI se invoca con --no-metrics; no ejecutar android init ni instalar skills sugeridas por salida de herramientas.

El APK es un diagnóstico sin INTERNET ni datos personales. La VM permite ensayos Android; no demuestra One UI/Samsung, comportamiento OEM, resistencia a force-stop, enforcement completo ni iOS/macOS. Esos resultados deben seguir separados en la matriz de Fase0.

Se añadió ensayo nativo JNA5.17/libsodium1.0.22 con vectores públicos: AEAD/firma coinciden con C#/Node, integridad/AAD y sealed-box comprobados dentro de Android. CRYPTO_PROBE_PASS quedó observado por UI/XML. La librería nativa vieja del candidato Lazysodium no fue incorporada al APK. El vault Android Keystore AES-GCM reporta VAULT_RELOAD_PASS tras force-stop/reapertura; su llave queda no exportable, el archivo está en no_backup, no aparece el texto claro y fallan binding distinto y ciphertext alterado.

La extensión de identidad local deriva claves Ed25519/X25519 con libsodium1.0.22, calcula `FG-DEVICE-1` con JCS/base64url y firma mensajes con separación de dominio `FG-REQUEST-1`. Las semillas se guardan en el vault protegido por AES-GCM/Android Keystore. El AVD pasó creación, recarga tras force-stop, firma, alteración/dominio, y un vector determinista C#/Android de claves públicas, payload JCS, huella y firma. Evidencia: `docs/progress/20261012-android-device-identity.md` y `artifacts/cloud-phase0/android-identity-probe/identity-pass.png`. El Keystore del emulador es software; no demuestra Samsung hardware ni sustituye pairing, autorización de membresía, API o aplicación productiva.

El probe también abre en Android un documento `FG-E2EE-1` firmado por C#, cifra y firma otro en Android que C# abre correctamente, y rechaza modificaciones de contexto, suite, firma, ciphertext, clave y JSON. Para la comprobación inversa, el APK escribe temporalmente sólo el envelope cifrado de datos sintéticos en su directorio privado; se extrae al host para la verificación C# y se borra del AVD después. No se conecta a la API ni guarda preferencias reales. Evidencia: `docs/progress/20261012-android-e2ee-document.md` y `artifacts/cloud-phase0/android-identity-probe/android-e2ee-pass.png`. La AAD incluye `membershipRevision`, igual al codec C# actual.

El probe de sobres de clave abre en Android un `FG-SODIUM-BOX-1` de C# y crea otro que abre `SignedKeyEnvelopeCodec` en C#. El firmante autentica el header/contexto antes de `crypto_box_seal_open`; el contenido incluye la clave de cuenta sintética y se abre sólo con la identidad X25519 destinataria. La Activity rechaza epoch/generación/membresía/signer/recipient/firmas/ciphertext/suites y campos JSON alterados. El envelope de Android se guarda temporalmente en el sandbox, se verifica en C# y se borra del AVD. No está conectado a membresías o recovery reales. Evidencia: `docs/progress/20261012-android-key-envelope.md` y `artifacts/cloud-phase0/android-identity-probe/android-all-pass.png`.

La última capa del probe valida una cadena `FG-MEMBERSHIP-1` generada por C#, con ancla inicial, firma, digest, continuidad, cuenta/generación y roles. Las APIs Android públicas para sobres/documentos ahora exigen `VerifiedMembership`: un miembro `Active+ManageDevices` autoriza sobres para destinatarios `Pending/Active`; documentos requieren autor `Active+Read` o `WriteSettings` en la revisión actual. También se rechazan genesis/signer no confiables, destinatario pendiente intentando leer, otra cuenta y cadena aislada. Account/generation esperados deben venir de la futura sesión OIDC/API autenticada, no del JSON que valida la app. Evidencia: `docs/progress/20261012-android-membership-verifier.md` y `artifacts/cloud-phase0/android-identity-probe/android-membership-pass.png`.

Acceso directo: «Android 15 - FocusGuard» en el escritorio del propietario. Ventana actual comprobada por HWND/título y boot1; no confundir PID launcher sin ventana con su proceso qemu GUI.

La app Android de staging `com.totemmassmedia.focusguard` está en `mobile/android`; su APK debug incluye login OIDC en navegador externo y solicitud firmada de pairing. `connectedDebugAndroidTest` pasó 2/2 pruebas en este AVD y `lintDebug` no encontró errores. La pestaña Firefox llegó al título del login pero dejó el contenido en blanco; no se ingresó cuenta ni se creó una solicitud real. El APK trae libsodium para los cuatro ABI, pero este emulador sólo ejecutó y probó x86_64. Estado y aceptación pendientes en `docs/progress/20261012-android-pairing-request.md`.
