# Prototipos aislados CLOUD-0

No integrados en FocusGuard.slnx, instalador ni servicio. No bloquean procesos, sitios o dispositivos.

## Ejecutar

Requisitos verificados en Windows de desarrollo: Node 24.19.0 y .NET SDK 10.0.302.

```powershell
npm ci --ignore-scripts
npm test
npm run validate
```

`npm test` compila el probe NSec y ejecuta 33 pruebas. Los bytes de claves del probe son material determinista público de test, jamás secretos de usuario. No genera credenciales de cuenta ni realiza llamadas a un cloud real.

Prueba Linux (WSL Ubuntu existente):

```powershell
wsl -d Ubuntu --exec python3 /mnt/e/FocusGuard/FocusGuard-Autopilot/prototypes/cloud-phase0/linux_probe.py
```

El probe abre un socket Unix temporal privado, se conecta desde un proceso hijo propio y comprueba SO_PEERCRED y permisos 0700/0600. Solo termina su propio hijo si falla la prueba; elimina el directorio temporal. No instala daemon ni cambia permisos ajenos.

## Cobertura y límites

- OpenAPI 3.1 con swagger-parser 13.1.0; JSONSchema Ajv 8.20.0, sin afirmar validación de todos los formatos ni campos cruzados en los schemas.
- Casos negativos de intent, roles/header, revisión y unknown fields.
- JCS con canonicalize 5.1.0: ordering, Unicode, números y valores no finitos. Duplicados JSON todavía requieren parser de entrada estricto; JSON.parse no los detecta.
- Referencia JCS C# fijada al commit32a4819, con licencias por archivo conservadas: pruebas cruzadas de Unicode/números/orden recursivo, rechazo de propiedades duplicadas/JSON malformado y UTF8 inválido. Se corrigió el input OEM de consola Windows mediante UTF8 estricto; no se sustituyeron acentos por ASCII.
- Admisión/ventana tardía/Strict y expiración de reservas: implementación de reglas en memoria, no persistencia SQL ni atomización distribuida real.
- AEAD XChaCha20-Poly1305 y Ed25519 interoperables NSec 26.4.0 / libsodium-wrappers 0.8.4.
- Linux: transporte real en WSL, no protección de aplicaciones ni GUI Avalonia probada.
- Pendientes: Kotlin/Android y Safari físico, vault/keyring, almacenamiento durable, shell/enforcement Linux de producción y recuperación instalada. HPKE provisional fue sustituido explícitamente por ADR0020.

## Ensayo Android compilado

`android/` contiene una Activity Java de capacidades API/permisos, no el cliente final Kotlin. Sin permiso INTERNET, sin envío de datos, sin AccessibilityService, VPN o enforcement. PACKAGE_USAGE_STATS solo se consulta para mostrar el estado de permiso propio; no se lee historial.

Toolchain portable utilizada en artifacts/cloud-phase0/toolchains: Microsoft OpenJDK21, Android platform35 y build-tools35.0.0. Archivos oficiales Google verificados contra checksums del repositorio; nada instalado globalmente.

Compilar mediante `android/build-probe.ps1` indicando JdkRoot, AndroidPlatformRoot, AndroidBuildToolsRoot y OutputDirectory. `-SignForLocalTesting` usa certificado de desarrollo desechable, no certificado de producción.

APK preparado: `artifacts/cloud-phase0/android-probe/focusguard-phase0-test.apk`; firmas v2/v3 verificadas. No instalado. La prueba física requiere copiar el APK al dispositivo con permiso del dueño, abrir «FocusGuard ensayo Fase 0» y registrar API/acceso de uso/overlay. No conceder permisos adicionales para intentar hacer aparecer resultados verdes.

Actualización posterior: APK actualizado con CryptoRuntimeProbe y JNA/libSodium1.0.22, instalado únicamente en Android15virtual autorizado, no en un teléfono. Prueba real mostró CRYPTO_PROBE_PASS. Preparar AARJNA verificado y nativos actualizados antes del build; el script ya exige esos insumos. Detalle/launcher en android/ANDROID-VM.md; el texto «no instalado» anterior describe la entrega inicial, no la VM actual.

## Ensayo shell Linux

LinuxShellProbe se publica self-contained para linux-x64, fuera de la solución de producto. Compila sin warnings. El primer arranque WSL falló por libICE.so.6 ausente; con libICE/libSM de paquetes oficiales descargados y extraídos únicamente en directorio temporal, el arranque real pasó (SHELL_WINDOW_OPENED y SHELL_EXIT_NORMAL, exit0). No se reemplazó por headless ni se instalaron paquetes OS. También falta ICU para ejecución normal de .NET; los probes usan invariant para este ensayo, no para certificar internacionalización.

Ejecutar `wsl -d Ubuntu --exec python3 /mnt/e/FocusGuard/FocusGuard-Autopilot/prototypes/cloud-phase0/linux_shell_probe.py` después de publicar shell y preparar los dos archivos .deb en artifacts. El runner elimina su directorio temporal al terminar. Abrir/cerrar ventana no equivale a probar enforcement, accesibilidad, recuperación o packaging de producción.

Sealed boxes: C#/Node y Android AVD validaron `FG-SODIUM-BOX-1` con la API oficial libsodium/JNA; C#↔Android pasa en ambos sentidos para un sobre firmado con binding y destinatario. Los codecs/probes son sintéticos: no prueban membresía/recovery completa, Samsung/ARM64 ni wrapper de producción. No es HPKE.

Paquetes fijados en package-lock.json y CryptoProbe/packages.lock.json. Restaurar con locked mode antes de distribuir; revisión de licencias transitivas y licencia raíz sigue pendiente. No distribuir estos prototipos como producto.
