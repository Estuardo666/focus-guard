# Auditoría de cierre — Fase0: contratos y decisiones

Fecha2026-10-11. Base de alcance: los ocho ítems y criterio de aceptación de sección16/Fase0 del PLAN-CLOUD-MULTIDISPOSITIVO.md. No redefinir cierre como implementación del producto cloud ni como tests de todos los OEM.

## Evidencia por requisito original

| Requisito | Evidencia inspeccionada/ejecutada | Resultado de Fase0 |
|---|---|---|
|1. Inventariar versiones/rutas/IPC/pruebas instaladas|README inventario, fuente Host/Client/Service/Contracts/IPC, manifests/roles/límite64KiB; lectura de service/VersionInfo y evidencias FG410 existentes; ausencia de rolSync confirmada|Inventario entregado; pruebas históricas no presentadas como reejecutadas|
|2. Formalizar autoridad/E2EE/identidad/consentimiento/reboot|ADR0016–0021, fin inmutable, motor local, normalInterrupted, Strict restore solo persistido/íntegro, consentimiento local y recovery separado|Decisiones formalizadas; futuras modificaciones SPEC/IPC enumeradas sin habilitar comportamiento instalado|
|3. OpenAPI/envelopes/canonicalización/límites/errores|CONTRACTS.md:31 operaciones/57schemas, validatorSwagger13.1, JSONSchemaAjv8.20 y33 tests; JCS C#/Node y Android, firma/AEAD/sobres y casos negativos|Contratos verificables entregados; no se anuncia API desplegada ni SQL/IdP reales|
|4. UX escrita login/settings/dispositivos|UX-FLOWS.md contiene cuenta/login/MFA/reset, perfil, bootstrap/QR, sync/conflictos, normal/Strict, simulaciónApple, errores/accesibilidad|Flujos escritos completos; pantallas de producto son fases posteriores|
|5. Bibliotecas mantenidas/licencias compatibles|ADR0021/DEPENDENCY-NOTICES, pins/locks, upstream de libsodium/NSec/JNA/canonicalize/Keycloak/Avalonia; referenciaJCS con sources/notices; Android1.0.22 ejecutado con JNA5.17, bundle viejo rechazado|Selección técnica y obligaciones cerradas para desarrollo; distribución pública/rootlicence/SBOM/hardening siguen enFase9|
|6. SpikeAndroid y shellLinux, límites reales|APK Java17 warnings-as-errors firmado v2/v3 instalado/abierto en Android15 AOSPvirtual autorizado; UI/XML CRYPTO_PROBE_PASS con versión1.0.22, cipher/firma cross, AAD/box tamper y JCS; WSL socket/SO_PEERCRED y ventana real abren/cierran|Spikes ejecutados; permiso no concedido/OneUI/ARM64/enforcement/recuperación no se atribuyen al resultado|
|7. Validar acceso físico y opciónApplePWA|Inventario basado en declaración del propietario: otroWindows hermano, iPhone15ProMax propio, S24 hermano/temporalmente ausente, WSL observado; Mac no reportado. VM instalada con permiso explícito como target disponible. PWA como vía sin membresía validada documentalmente, límites Safari/background y distribución Apple escritos|Disponibilidad/ausencias y alternativa viables documentadas; validar disponibilidad no equivale a conectar/probar todos los teléfonos|
|8. InventarioZDR/persistencia/capturas de proveedores|PLATFORMS-AND-DEPENDENCIES y plan10.7: categorías excluidas, ciphertext/metadatos retenidos, IdP/API/DB/proxy/APM/mail/push/backups/clientes y retención; sin ZDR total|Inventario entregado; proveedor/config runtime y purga/restore son fases de operación posteriores|

## Criterio de aceptación: semántica sin ambigüedad

- Fin local: UTC/monotónico, deadline revisado inmutable y consentimiento máximo, sin extensión por retries/reconexión; reloj inválido no admite. Reserva finita/inmutable/no posterior al fin.
- Parada: normal_stop terminal; Strict rechaza stop/skip, cancel afecta solo Preparing/Ready; cancel/commit CAS y estados terminales no reviven.
- Revocación: niega futuras operaciones/lecturas/tickets, rota época; mantiene fin/recovery local y no promete borrar copias offline.
- Simulación: macOS/iPhone siempreSimulated, sin bloqueos OS ni contador de protección real. Permisos/canales faltantes se declaran.
- Reboot normal: no restaurar, persistir interrupción/tombstone; Strict conserva solo ejecución ya aceptada con integridad y fin original.

Esas reglas figuran enADR/contratos y los33 tests de prototipo cubren sus bordes relevantes. Recuperación/enforcement productivos no se fingen mediante el modelo en memoria.

## Validaciones de cierre observadas

1. Último npm test:33/33, build .NET0warnings/0errors; npm run validate:OpenAPI3.1 passed.
2. Android virtual API35/boot1/ActivityStatusok; XML real confirmó CryptoProbe + JCS pasando. APK SHA2560F5F440E8521B724A98B902E3C00F502103E1F72F84E8A39E0D58F29D8594770, firmado y validado. Samsung real no disponible, no instalado allí.
3. Linux PythonUnix probe pasa; shell real emite SHELL_WINDOW_OPENED/SHELL_EXIT_NORMAL, exit0 con deps portables. ICU/internacionalización y distro/Wayland/accessibility futuras no certificadas.
4. GUI Android con ventana/serial propios, launcher Status/Show/Stop verificados; fuentes de producto src/tests/extension/scripts sin diff. Installer no se reconstruye por componentes no distribuidos.
5. Fuentes de licencia/ref vendored y cambios JavaBoolean declarados; no downgrade warnings ni reemplazo de pruebas por mocks para aprobar.

## Trabajo que permanece en otras fases

Fase1: login/identidad y cuenta reales. Fase2: vault, membresía/kit/revocación/sync reales y módulointerop endurecido/paquetes Android reproducibles. Fase3/4/4B: ajustes, IPCSync y bloqueo remoto aplicados por motores. Fase5: companionApple sincronizado. Fase6/7/8B: enforcers, OEM/ARM64/Doze/force-stop, recovery/estricto y cinco clientes. Fase9: distribución, rootlicense/SBOM/legal/privacidad de proveedor, backups/purga, pruebas de fuga y auditoría externa.

Estos gates ya pertenecían al plan completo; no se eliminan ni se declaran logrados. No mantener Fase0 bloqueada esperando implementar todas esas fases. Tampoco usar el cierre de Fase0 como permiso para desplegar, publicar o instalar en equipos ajenos.

## Dictamen

Fase0 cumplida como **contratos, decisiones, selección y spikes** del alcance original. Evidencia virtual y física se distinguen. Producto cloud todavía no implementado, seguridad productiva no certificada y aplicaciones instaladas Windows intactas. Siguiente fase prevista:Fase1 identidad/cuenta, sin ejecución automática de despliegue.
