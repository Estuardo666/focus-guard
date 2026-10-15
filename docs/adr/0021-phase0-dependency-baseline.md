# ADR0021 — Baseline de dependencias y validación Fase0

Fecha2026-10-11. Estado: decisión técnica de Fase0, sustentada en código y runtime; no autorización de distribución productiva ni implementación de cuentas/bloqueo cloud.

## Selección

- Identidad: Keycloak OIDC autoalojado, Apache-2.0. Versión operacional/hosting a fijar en Fase1; no añadir proveedor comercial ni engine de passwords propio.
- .NET: NSec.Cryptography26.4.0 con motor libsodium1.0.22, MIT/ISC, lockfile. AEAD XChaCha20-Poly1305 y Ed25519; sealed-box mediante adaptador CAPI explícito según ADR0020, no fingir API managed NSec inexistente.
- JavaScript: libsodium-wrappers0.8.4 ISC, canonicalize5.1.0 Apache-2.0, lockfile. No Sodium.Core legacy para proyecto nuevo.
- Android: motor libsodium1.0.22 ISC y JNA5.17.0 bajo alternativa Apache-2.0 de su licencia dual. Adaptador CAPI pequeño mantenido por FocusGuard; no implementar primitives criptográficas propias. Runtime Android15x86_64 comprobó versión/cipher/firma/roundtrip/tamper. Lazysodium5.2.0 con bundle1.0.20 no se selecciona como paquete nativo final.
- JCS C#/Java: fuente de referencia cyberphone/json-canonicalization pin32a48195b9b20fe67b1795e39e4509f14d860b45, mantenimiento controlado en repositorio y pruebas de compatibilidad. C# conserva notices Apache/BSD/MPL2/Lucent; Java Apache2 con modificación local Boolean.valueOf declarada. No adoptar NuGet sin procedencia/licencia suficiente ni el JAR Maven2018 como versión corriente.
- Shell Linux: Avalonia12.1 existente, MIT. Spike WSL real de ventana y socketUnix validado; packaging/keyring/intl/enforcement separados.

## Compatibilidad, mantenimiento y límites de adopción

La combinación permite la evaluación privada y desarrollo del prototipo conservando derechos y notices; no se añade licencia raíz al proyecto ni se afirma certificado legal de distribución. Mantener fuentes/notices de componentes MPL2 y sus cambios al redistribuir; elegir Apache2 para JNA, sin imponer LGPL por error. Revisar licencia raíz/obligaciones antes de publicar en Fase9. Esa decisión comercial de publicación no convierte la selección técnica Fase0 en una distribución ya autorizada.

Mantener pins y locks, fuente/procedencia y hashes nativos; consultar advisories antes de cada release. Actualizaciones se aceptan solo con tests de ciphertext/firma/JCS/tamper y versiones runtime. Fuente vendored requiere ownership de mantenimiento de FocusGuard, no promesa de paquete externo activamente actualizado.

Los assets Android del paquete interno libsodium restaurado por NSec sirvieron para evaluación; no referenciar ese paquete como API Android ni convertir copia de cacheNuGet en pipeline productivo. Fase2 debe fijar distribución/source-build nativo mantenido y reproducible, notices, carga de biblioteca protegida, buffers y ABI/page-size ARM64. Los probes son evidencia de viabilidad y elección, no sustitutos de hardening.

## Evidencia

33 tests Node/C# y OpenAPI31 operaciones/57schemas; Android UI mostró CRYPTO_PROBE_PASS con1.0.22, AEAD/firma iguales a vectores Node/C#, AAD/box alterados rechazados y JCS Unicode/números/orden/duplicados. Compilación Java warnings-as-errors, APK firmado v2/v3, instalación y Activity reales en VM autorizada. Runner shell WSL abrió/cerró realmente; socket privado/SO_PEERCRED reales.

## Frontera de fases

Fase0 decide contratos, dependencias y viabilidad mediante spikes. No exige construir backend/credenciales/vault/recovery/remoto completos que el plan asigna a Fases1–9. Mantener gates de esas fases sin anticiparlos como hechos cumplidos ni trasladarlos artificialmente a un bloqueo indefinido de Fase0.

Emulador autorizado por propietario es target del spike Android genérico. No sustituye afirmaciones sobre Samsung/OneUI/ARM64/Doze ni disponibilidad real del teléfono: esos escenarios siguen en Fase6/9. El inventario de acceso físico registra lo comunicado por dueño y disponibilidad real; Mac no reportado, Samsung temporalmente ausente, iPhone propio y otroWindows reportados. No inventar prueba física para cerrar un inventario.
