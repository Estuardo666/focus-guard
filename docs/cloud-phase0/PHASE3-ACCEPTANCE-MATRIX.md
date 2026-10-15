# Fase 3: matriz de evidencia y gates restantes

Auditoría actualizada al candidato `.179` (15 de octubre de 2026). Plan: contrastar los cinco entregables normativos con implementación, pruebas y aceptación observable. Esta matriz no convierte pruebas locales en aceptación física.

| Requisito del plan | Implementación/evidencia inspeccionada | Estado demostrado | Gate restante |
| --- | --- | --- | --- |
| 1. Documentos cifrados CAS/ETag | `SettingsHttpTransportTests`: ciphertext canónico y proof con digest del token; lectura comprueba ruta/revisión/ETag; `SettingsSyncWorkerTests`: preparación durable y readback. Harness `artifacts/phase3-175-keycloak-tests.log` termina en 152 checks con API/PostgreSQL, JWT Keycloak y DPAPI. | PASS local; API y cifrado reales en fixture aislado. | Convergencia física de ambas PCs; después, cursor/backend desplegado. |
| 2. Snapshot/cursor, outbox y avisos | `SettingsSyncWorkerTests`: cursor durable/rechazo de retroceso, fallback 404, carrera edición/cursor, receipt sin eco mantiene Pending. `SettingsOutboxTests` y retry checkpoint prueban leases, ACK perdido y persistencia. UI expone Pending/Conflict/Attention. | PASS local, incluido Host y UI. | Observar Pending/reconexión en las dos instalaciones. |
| 3. Edición offline, conflictos y tombstones | `SettingsReconciliationTests`: merge de tres vías, conflicto de duración, borrado que no resucita, ACK exacto. `HostSettingsSyncHttpsTests`: dos sesiones sobre HTTPS con CAS real, resolución y reset, reanudación DPAPI. El propietario reportó conflicto `60 vs 40` resuelto eligiendo Esta PC y reset normal propagado. | PASS local; duración offline y reset normal reportados PASS. | Ejecutar conflicto de tombstone/reset offline usando el prepaso nuevo del runbook. |
| 4. Overrides locales y Pending | `LocalSettingsCoordinatorTests`: reinicio, remoto y retirada, edición mixta, reset/tombstone y stale review. `HostLocalSettingsOverridesTests`: bridge/dialog presenter acepta/cancela/rechaza draft cambiado sin transport writes. `HostSettingsSyncHttpsTests`: los tres overrides de un Host sobreviven al reinicio, a una revisión distinta y al tombstone del peer; retirarlos aplica la última revisión/defaults sin POST. `AccountPanel.test.tsx`: summary y acción solo con sync consentida/estable. | PASS local; controles incluidos desde `.175`, incluyendo convergencia HTTPS dual-Host y reset para los tres campos. | Selección/cancelación nativa, persistencia, independencia y retirada en PCs físicas. |
| 5. Exportar/restablecer explícitamente | `SettingsExportTests`: confirmación, allowlist, selección y rechazo de JSON no admitido. `HostPortableSettingsTests`: export/import sin cuenta, cancelación, archivo inválido/excesivo, stale preview y corrupción conservada. Teoría de creación productiva Host importa duración y observa publicación cifrada. Reset tiene preview nativo y comprobación del estado bajo lease. | PASS local, incluyendo camino de servicio con sync activa. | Diálogos de archivo y reset observados en equipo; importación parcial y respeto de excepciones. |
| Aceptación: tema, idioma y duración convergen entre dos Windows | Host259/259, Sync160/160, UI254/254 y harness152 son evidencia local de `.179`. Las regresiones cubren importación portátil con consentimiento revocado y precedencia/restauración HTTPS de override entre dos Hosts. El propietario reporta los tres campos convergidos en 30–45 s y cuenta/vínculo/consentimiento conservados al reabrir ambas PCs. | PASS reportado para convergencia, persistencia de sesión y consentimiento. | Reset offline/tombstone, override local tras reiniciar, portátil en receptor y comparación de reglas/permisos requieren la PC hermana. |
| Aceptación: rutas de apps y permisos no se copian | Los payloads globales contienen solo los tres ajustes; tests de proyección/import preservan enforcement y campos locales. | PASS local; Fase 2 tuvo aceptación física de transferencia manual, distinta de sync automática. | Comparar reglas, perfiles, permisos y demás preferencias antes/después del ensayo automático. |
| Cierre/reapertura | El propietario informa que ambas PCs conservaron sesión, vínculo y consentimiento tras cerrar y abrir FocusGuard. | PASS reportado en ambas PCs. | Registrar versiones/hashes y completar los gates físicos restantes. |

## Evidencia de distribución

`artifacts/phase3-179-build.log` registra construcción productiva y verificaciones embebidas. `artifacts/phase3-179-installer-tests.log` registra PASS; guard de producción y self-test ejecutados. Hashes revalidados:

- Setup `.179`: `F4F174BE03E89ED228CF9C34A2C0FA202BAF9BB7F097F10E94B0AEE3F1B31D91`.
- ZIP menú `.179`: `BCD9CFA04384CD92F4C29497D97290ED4B3A82D43F77A18CD0879896EA90271B`.

El paquete `.179` está generado; nada nuevo se instaló durante este checkpoint. Después de que `.175` o posterior escriba estado de sync, volver a `.174` detiene sync conservando el archivo; recuperar con `.175` o posterior. No degradar validaciones ni borrar estado para pasar una prueba.

## Resultado y próximo paso

Los cinco entregables tienen implementación integrada y evidencia local. El propietario reportó convergencia física de los tres campos, cierre/reapertura, conflicto offline de duración y reset compartido normal. Quedan reset offline/tombstone, override físico, portátil en el peer y comparación de preferencias locales; la segunda PC ya no está disponible. Staging sigue detrás del gate físico, según el plan. No repetir suites verdes sin un cambio o hallazgo que lo justifique.

Siguiente acción: cuando vuelva a estar disponible la PC hermana, ejecutar los gates físicos restantes en `PHASE3-PHYSICAL-ACCEPTANCE.md`; hasta entonces pueden avanzarse auditorías y correcciones locales. No simular aceptación ni saltar el gate de staging.
