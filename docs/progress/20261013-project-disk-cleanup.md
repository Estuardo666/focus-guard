# Limpieza del proyecto — 2026-10-13

Solicitud: liberar espacio en C:, D: y E:. Plan: verificar rutas y contenido, eliminar paquetes obsoletos/cachés regenerables y comprobar conservación del código y evidencias.

Eliminado aproximadamente: **D: 69,70 GiB; E: 5,53 GiB; C: 0,64 GiB; total: 75,87 GiB**. Las primeras mediciones fueron redondeadas. Espacio libre final: C: 14,58 GiB; D: 109,39 GiB; E: 94,37 GiB. Puede variar por otras aplicaciones.

- D: `setup-publish`, `setup-payload` e `installer` del respaldo `FocusGuard-E-artifacts-archive-20261009/artifacts`: 32,88 GiB.
- D: directorios `publish` y ejecutables/ZIP antiguos en `repo-pass2-20261009/artifacts` y `focusguard-fg166-validation/artifacts`: 36,822175 GiB. Inventario individual en `20261013-cleanup-candidates.json`; comprobados ausentes tras eliminación.
- E: `installer-r16`, `installer-r17`, `fg408-output`, ZIP e instalador .138. Conservados paquetes .156, .157 y .158.
- C: `node_modules` y `.next` de `.codex/focusguard-staging-build/portal` y caché Gradle `8.14.6/transforms`. Se regeneran al instalar dependencias o compilar.

Sin cambios de código. Verificada existencia de `src`, `tests` y `.git` en E:, y `src`/`tests` de la validación antigua en D:. Conservados datos de usuario, claves, configuración, SDK Android, AVD activo, capturas y registros de los respaldos inspeccionados. Las compilaciones eliminadas requieren regeneración si vuelven a necesitarse; los checkpoints históricos pueden referenciarlas.

No corresponde reconstruir instalador ni ejecutar pruebas de código: ningún componente distribuido cambió. Fase 2 y pruebas físicas pendientes conservan su estado.

## Segunda limpieza E: autorizada
Inventario: 72 objetivos, 8,853 GiB. Eliminados 4,547 GiB según diferencia del inventario y archivos restantes; 17 objetivos ausentes y 55 pendientes (4,307 GiB). Espacio libre observado: 100,95 GiB. Falló Remove-Item al eliminar un directorio generado android-x86 dentro de bin de Host.Windows. El reintento con Force fue rechazado por política automática; no se intentó evadir la restricción. Fuentes, pruebas, Git, emulador actual e instaladores .157/.158 verificados presentes. Inventario y resultado: 20261013-e-cleanup-second-inventory.json y 20261013-e-cleanup-second-result.json. Sin cambios de código.

### Reauditoría de rutas — 2026-10-14

Las 72 rutas del inventario (todas bajo la antigua ubicación E:) devuelven ausente hoy; también `E:\FocusGuard\FocusGuard-Autopilot` y los dos respaldos D: enumerados en `docs/progress/20261013-cleanup-candidates.json`. No medí variación de espacio libre ni atribuyo la ausencia a una eliminación específica. Los seis volcados UI nuevos y los paquetes .160 permanecen; la revisión automática rechazó el borrado de los volcados.
