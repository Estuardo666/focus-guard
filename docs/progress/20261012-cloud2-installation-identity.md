# Fase 2 — identidad de instalacion

## Plan de ejecucion autorizado

Requisitos completos: identidad criptografica, bootstrap primer equipo y kit recovery, pairing QR/codigo/huella/aprobacion, membresia firmada/CAS/epochs/revocacion, vaults por plataforma/vectores y migracion consentida. Aceptacion final exige segundo Windows descifrando documento, aislamiento de cuentas, rechazo de QR replay y password reset sin acceso a contenido.

Primer checkpoint: libreria de identidad de instalacion con Ed25519 y X25519 separados, fingerprint determinista y vault Windows DPAPI ligado a issuer/sub/generacion/deviceId. No crear membresia activa ni vincular equipo solo por generar claves. No tocar bloqueo local. Pruebas: firmas falsas/dominio/contexto incorrecto, independencia de claves, corrupcion DPAPI, mezcla cuenta/generacion/deviceId, persistencia y concurrencia de escrituras.

Siguientes checkpoints: solicitud/aprobación visible del pairing en Windows; endpoints de documentos cifrados con CAS; migración consentida de ajustes; vault/interoperabilidad Android y pruebas físicas. El Samsung y segundo Windows ya mencionados se usarán cuando corresponda sin pedir credenciales por chat.

## Seguridad y recuperacion

Claves no derivadas de password OIDC. DPAPI protege la identidad Windows y los secretos ligados a cuenta/generación. El kit sólo se muestra en diálogo nativo tras acción explícita; React, API y logs no reciben el kit ni semillas privadas. La API exige prueba de posesión en bootstrap/pairing, pero aún no hay autorización para contenido compartido ni órdenes remotas. No ampliar a órdenes de fases 4/4B.

## Estado

Fase 2 en curso. Bootstrap inicial, revocación de membresía y codec local de documentos están implementados; el paquete no se ha instalado ni se ha vinculado una cuenta real desde el Host. No afirmar sincronización ni aceptación final de Fase 2.

## Resultado checkpoint 1

Implementados cloud/devices/Core (Ed25519/X25519, identidad publica/huella JCS, contexto issuer-sub-generation y DPAPI), Canonical (upstream fuentes/licencias) y Tests. 11/11 PASS Release, cero warnings finales. Pruebas negativas de firma/dominio/clave/ciphertext, sustitucion de cuenta/generacion/deviceId, cancelacion y dos escritores concurrentes. Dependencia NSec26.4.0 fijada con lock; API de claves verificada en docs oficiales. Error xUnit2031 corregido usando overload Assert.Single con predicado; rerun PASS.

No hubo cambios Host/UI/API ni recursos cloud en el checkpoint 1. DeviceLinked no se modificó y no se generó kit sin consentimiento.

## Resultado checkpoint 2 — bootstrap Windows y consentimiento de recovery

Plan: integrar el primer bootstrap en una acción explícita del Host; no incluir secretos en React; presentar el kit antes de enviar el alta; guardar identidad/kit/clave en vault antes de la transacción remota para permitir reintento; validar el protocolo real contra Keycloak y Neon; reconstruir el instalador standalone.

Implementado: `HostAccountService` consulta el contexto de cuenta por el API HTTPS configurado, crea identidad/kit/claves sólo después de que el usuario pulse «Vincular este equipo», muestra una ventana WinForms con kit seleccionado manualmente y exige marcar que se guardó antes de continuar. Luego persiste el material cifrado con DPAPI y envía challenge, prueba Ed25519, membresía inicial firmada y sobres libsodium separados para dispositivo/recovery. Un resultado de red ambiguo se puede reintentar desde el vault local. El puente expone sólo estados `checkingDevice`, `deviceLinked`, `deviceLinkPending` y errores con códigos acotados. No cambia sesiones locales ni habilita bloqueo remoto.

El vault ahora lleva una cabecera pública mínima con issuer, binding opaco, IDs y deviceId para localizar y reanudar el archivo; todos los campos privados y el recovery kit siguen dentro de DPAPI CurrentUser, con entropía ligada al contexto completo. Se verifica que no haya semillas/kit en claro, y se rechazan sustitución de cuenta, generación, issuer o deviceId. El Host incorpora Core/Windows y `libsodium.dll` al paquete.

Validación: `dotnet test cloud/devices/Tests/FocusGuard.Devices.Tests.csproj -c Release -m:1` 12/12; `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release -m:1` 204/204; `npm test -- --run` 229/229; `Test-KeycloakNeon.ps1 -NativeOnly` 88/88 con cuenta sintética, Keycloak real, Neon y Windows DPAPI; `Test-TncStaging.ps1` 51/51; `scripts/test-installer.ps1` y `--package-self-test` pasan; Setup standalone 0.1.150 y ZIP tienen hashes embebidos del Host verificados. Setup SHA-256 `5DF83FBCFE6645F7369B645866194CBC9329FCB14F24C9B8B40989A0BE369FEE`; ZIP SHA-256 `8AADE014DE644A664100F0618051A7093C8DD013EEA99FE62D1CFFD233311FC2`. El publish contiene `FocusGuard.Devices`, `FocusGuard.Canonical`, `NSec.Cryptography` y `libsodium.dll`. `graphify update .`: 19,549 nodos, 91,501 aristas y 1,399 comunidades; se omitió sólo la visualización HTML por superar el límite de nodos.

El volumen E: (exFAT) informa `HealthStatus=Warning` y `OperationalStatus=Full Repair Needed`; por eso el segundo build UI se generó en una carpeta temporal NTFS de C: y se copió al `dist` existente sin limpiarlo. La instalación no se ejecutó. El usuario debe instalar/revisar 0.1.150 y confirmar el kit en el diálogo para crear su primer vínculo real; ningún alta se envió durante este checkpoint.

## Resultado checkpoint 3 — revocación transaccional y rotación de epoch

La API añade `POST /v1/memberships/devices/{deviceId}/revoke`. Requiere correo verificado, login vigente, firma de dispositivo activo con `ManageDevices`, `requestId` no repetido y membresía exacta sobre la revisión actual. La transición sólo puede marcar Revoked al destino indicado, conservar sin cambios las demás claves/roles/estados, conservar un administrador activo, avanzar revisión y epoch, y adjuntar sobres firmados para todos los miembros que sigan activos y recovery. En la misma transacción CAS, actualiza la membresía, revoca las sesiones de transporte del destino y cancela sus solicitudes pendientes. El endpoint no acepta JWT de cuenta sin prueba de posesión.

Validación integrada contra Keycloak real, Neon y DPAPI: 93/93. El fixture creó dos dispositivos, aprobó y confirmó el segundo, revocó el segundo desde el primero, verificó `revoked_at`, abrió sobres de la clave rotada sólo con el dueño y recovery, rechazó el replay y mantuvo el dispositivo restante activo. Se desplegó API `tnc-api-staging-00008-8wd`, digest `sha256:045b7dabd2696d658eefdc3079975a8dba404196595d6f51fc8595d2d5ff7df8`; health live/ready 200 y `Test-TncStaging.ps1` 52/52.

## Resultado checkpoint 4 — documentos de contenido XChaCha

`FG-E2EE-1` ya tiene codec C# para JSON JCS ≤256 KiB, XChaCha20-Poly1305-IETF, AAD de cuenta/generación/entidad/tipo/revisiones/epoch y firma Ed25519 `FG-DOCUMENT-1`. Decrypt verifica contexto y firma antes de AEAD; el caller debe comprobar membresía del autor por separado. P/Invoke inicializa libsodium y borra copias de claves, nonce, AAD, texto y ciphertext en rutas de salida.

El vector `content-document.test.mjs` intercambia documentos firmados C#/Node en ambas direcciones y rechaza contextos, autores, claves y ciphertext incorrectos. `dotnet test cloud/devices/Tests/FocusGuard.Devices.Tests.csproj -c Release -m:1` pasa 14/14; `npm test` en `prototypes/cloud-phase0` pasa 36/36. Además un test con dos identidades locales confirma envelope de clave → descifrado de documento y rechazo con una clave nueva después de reset; es verificación criptográfica local, no segundo Windows físico ni prueba del flujo real de reset.

Pendiente para aceptación: Host UI para solicitud y comparación/aprobación de la huella del segundo Windows; endpoints de lectura/escritura de documento CAS detrás de prueba de posesión; sincronización de membresía/clave epoch en el vault de cada dispositivo; migración de ajustes con consentimiento; vault e interoperabilidad Android/S24 y pruebas físicas. No se ha vinculado la cuenta real, instalado el setup 0.1.150 ni compartido un documento por la nube.

