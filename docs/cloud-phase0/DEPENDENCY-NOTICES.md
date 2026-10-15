# Inventario de licencias de Fase0

Evaluación/desarrollo privados; no publicar ni cambiar licencia raíz del producto mediante esta lista.

| Componente | Pin/procedencia | Licencia | Tratamiento |
|---|---|---|---|
| NSec.Cryptography |26.4.0 NuGet/lockfile|MIT|Conservar notices al distribuir|
| libsodium nativo |1.0.22, authorFrankDenis, hashes/versión comprobados|ISC|Conservar copyright/licencia y fijar pipeline productivo|
| libsodium-wrappers |0.8.4 npm/lockfile|ISC|Notice y controles de bundle|
| canonicalize |5.1.0 npm/lockfile|Apache2|Notice/licencia|
| JNA Android |probe 5.17.0; app staging 5.19.1 Maven AAR SHA-256 `B57125CB7D16253F0D65A80F7D3A4C3664EFFA711B8BDBB7F87FB572CE1624ED`|Apache2 o LGPL2.1+|SeleccionarApache2; preservar notices y revisar transitivas|
| JCS C# reference |commit32a4819, ocho fuentes|Apache2/BSD/MPL2/Lucent según archivo|Fuentes/notices originales retenidos; textos completos incluidos; no relabelApache-only|
| JCS Java reference |mismo commit, tres fuentes|Apache2|Notice/archivo de modificación Boolean.valueOf y texto completo|
| Avalonia Desktop |12.1.0 lockfile|MIT|Conservar notices|
| SwaggerParser / Ajv |13.1.0 /8.20.0 npm|MIT|Herramientas de tests; revisar todas transitivas antes de distribución|
| Keycloak |upstream/source LICENSE|Apache2|Versión de servicio y transitive audit en Fase1/9|
| SDK/emulator/system image |catálogos Google verificados|Términos de desarrollo SDK/AOSP y notices de paquetes|Solo tools locales; no redistribuir como parte de FocusGuard|

Archivos de notices completos JCS: prototypes/cloud-phase0/JcsReference y android/jcs-reference. POMs/metadata primarios y hashes de otras dependencias figuran en checkpoints; las licencias por componente no son garantía legal universal ni auditoría supply-chain completa.

Fuentes de SPDX/metadata: registros oficiales npm/NuGet/Maven y licencias/repositorios primarios enlazados en PLATFORMS-AND-DEPENDENCIES.md y ADR0021. Antes de beta pública generar SBOM y revisar NOTICE completo/transitivas, root licence y subprocesadores; no afirmar esos gates futuros ejecutados.
