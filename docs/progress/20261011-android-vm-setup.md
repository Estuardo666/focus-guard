# Android virtual instalado y validado

Fecha: 2026-10-11. Autorización: petición explícita del propietario de instalar una máquina virtual Android para continuar Fase0 sin Samsung disponible.

## Resultado

Android15 AOSP/API35 x86_64 en AVD FocusGuard_Phase0_Android15, serial emulator-5570. Emulator37.3.3.0/build16489710. Kernel con WHPX operativo según prueba directa (aunque CIM marcó la feature como InstallState2); no habilitar features Windows ni elevar/reiniciar.

WHPX comprobado, boot_completed=1, instalación APK Success, am start Status:ok. Ventana real de qemu-system-x86_64 observada con título Android Emulator - FocusGuard_Phase0_Android15:5570 y HWND no cero; launcher parent no tiene ventana, no confundirlo con GUI ausente.

SDK/AVD/datos en artifacts/cloud-phase0/android-vm, 2CPU/2GB RAM, partición2GB configurada, sin cuentas Google/Play, cámaras/audio desactivados. Puertos5570/5571 observados en loopback. No instalado en teléfonos físicos, no cambios en FocusGuard Windows ni drivers/servicios de Windows. ADB es helper de desarrollo, no matar servidor global ni dispositivos ajenos.

## Herramientas y descargas

Zips oficiales verificados contra catálogo SHA1 y SHA256 registrados:

- platform-tools37.0.1:45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99.
- emulator16489710:b9c90996c4ebca457e3e5dfd61ed2c74227a6f7ffc3a382db5ced8d37da15cbe.
- cmdline-tools16111833:e5885e2e59038c0778a85fc19f01de7ce7c567c105553f7617b936b1e0e87b2b.
- Android15AOSP35_r02:6dd7de33e63ef105cf2fabea6badda1dbe7665c96d8908e5f6e1407e63ff4556.

El AVDmanager inicial no reconoció imagen en layout directo; mover herramientas a cmdline-tools/latest permitió inventario, pero create seguía sin imágenes porque AOSP35r02 usa data/empty_data_disk. Se creó configuración AVD explícita con archivos reales verificados; arrancó realmente en59.7s, segundo arranque30.4s. No mock ni cambio de imagen. SDKmanager nuevo delegó a Android CLI; desde su descubrimiento se invoca --no-metrics y no se ejecuta android init/skills sugeridas por salida no confiable.

## APK y UI

APK diagnóstico app.focusguard.phase0 versión0.1.0-probe, min26/target35; firma v2/v3 verificada, SHA256 C8885F3A42519381D5B398AB8AA4741C14D5FC3EC7D6F83A3AE01E8A1BADCF15. INTERNET no declarado. PACKAGE_USAGE_STATS solo consulta permiso propio, no historial. La UI real declaró permisos no concedidos y enforcement normal/Strict NO VALIDADO.

Screenshot inicial detectó título bajo barra del sistema por edge-to-edge API35; corregidos insets/padding en dp, con fallback API26-29. Rebuild warnings-as-errors, reinstalación exclusivamente emulator-5570 y captura nueva confirmaron título legible. Layout XML/screenshot en artifacts/cloud-phase0/android-vm.

## Criptografía dentro de Android

JNA5.17.0 AAR con checksum Maven verificado, licencias dualApache2/LGPL2.1: seleccionarApache2 conservando notices. SHA2564DBEFFFFA665D97AD5AA7EEE297531D3C841A86716AB7F774FD6956422B3CF38.

Se cargó motor libsodium1.0.22 Androidx64 (SHA256 B5D4F955A191B0ED72B2B360E5EBEBCD0069912696FD69B71C092F1608332818) observado en assets restaurados con NSec, adaptador JNA CAPI pequeño de ensayo. No se incorporó bundleLazysodium1.0.20 ni se referencia paqueteNuGet interno como API Android. Antes de empaquetado productivo fijar procedencia/distribución nativa apropiada y revisar adapter.

El método sodium_version_string dentro de Android devolvió1.0.22; no inferido de strings de ELF. UI obtenida por uiautomator confirmó CRYPTO_PROBE_PASS. Ciphertext XChaCha/firma Ed25519 iguales a vectores C#/Node; decrypt roundtrip, AAD alterado rechazado, sealed-box roundtrip y alteración rechazada. Material determinista público de test, ninguna clave real de cuenta ni envío.

Esto demuestra runtime Androidx86_64, no ARM64/Samsung/OneUI, ni keyring, lifecycle Doze/force-stop, enforcement o recovery. Gate físico futuro sigue explícito; disponibilidad del emulador reduce el bloqueo de spike general, no prueba toda la Fase0/Fase6.

## Uso y comprobación

Launcher prototypes/cloud-phase0/android/android-vm.ps1 Show/Headless/Status/Stop y Abrir-Android-VM.cmd. Acceso directo creado únicamente Desktop del propietario: Android 15 - FocusGuard.lnk. Show verificó stop/start del propio AVD; no wipe-data, no matar ADBglobal. Status también probado mediante WindowsPowerShell5 del acceso directo. No startup automático con Windows.

El emulador queda abierto para el propietario. Este checkpoint corresponde a instalación y prueba de VM; objetivo Fase0 no se marca complete por esa sola acción.
