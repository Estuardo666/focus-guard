# Fase 3: resultados físicos Windows .175 reportados por el propietario

Fecha: 15 de octubre de 2026. Evidencia: reportes directos y capturas aportadas en este chat, no ejecución remota del agente.

| Ensayo | Observación | Resultado |
| --- | --- | --- |
| PC hermano actualizada | Propietario instaló .175; sesión iniciada y dispositivo todavía vinculado. | Vínculo conservado reportado. |
| PC propietario al preparar | Apareció sin sesión y volvió a iniciar sesión. | Incidencia de identidad pendiente de diagnóstico; no atribuir causa a update sin evidencia. |
| Consentimiento | Ambas mostraron sync activa; receptor mostró tres campos siguiendo compartidos. | Consentimiento observado. |
| Conflicto inicial duración | Propietario 17 vs global 50; tras elegir This PC, origen mostró Enabled; receptor aún mostraba su valor previo 13. | Conflicto revisado, convergencia visible en caliente falló. |
| Reabrir receptor | Al cerrar/reabrir, duración, tema e idioma quedaron sincronizados según reporte. | Recepción y persistencia observadas; no equivale a actualización en vivo. |
| Segundo cambio idioma con ambas abiertas | Vista de bandeja del hermano cambió, ventana principal conservó idioma anterior. Al reabrir quedó en español. | Fallo reproducible de refresco de ventana principal. |
| Repetición en vivo de idioma después del fix | Tras instalar `.176`, el propietario cambió el idioma desde su PC y confirmó que se reflejó en la ventana del receptor sin reabrirla. | PASS reportado; demora observada en la segunda explicación. |
| Tema/idioma/duración tras el cambio en vivo | El propietario aclara que los tres se actualizaron automáticamente en aproximadamente 30–45 segundos en las PCs de la prueba; no fue necesario navegar por el sidebar ni cerrar/reabrir. El clic coincidió con el refresco. | PASS reportado para convergencia automática en los tres campos dentro del objetivo. Versión `.176` del receptor reportada; anotar versiones/hashes de ambos equipos al cerrar la prueba. |
| Cerrar/reabrir ambas PCs | El propietario cerró y volvió a abrir FocusGuard en las dos PCs; confirmó que sesión, vínculo y consentimiento siguen guardados. | PASS reportado en ambas PCs; no usar Sign out. |
| Reconexión/conflicto offline | Primero se observó «Los ajustes locales se guardaron y esperan sincronizarse». Tras realizar la segunda edición offline y reconectar, apareció el conflicto `Esta PC: 60` vs `Otros dispositivos: 40`. | El conflicto offline fue generado y presentado al propietario. |
| Conflicto offline de duración visible/resuelto | Captura mostró `Esta PC: 60 minutos` y `Otros dispositivos: 40 minutos`. El propietario eligió `Esta PC` y reporta que sincronizó bien. | PASS reportado para elección local y publicación. Verificar valor final en ambas PCs al continuar. |
| Resolución del conflicto offline de duración | El propietario eligió `Esta PC` y reporta que sincronizó bien. El valor de origen era 60 min frente a 40 min de otros dispositivos. | PASS reportado: elección local aplicada y sincronizada. Verificar valor final en ambas PCs al continuar. |
| Aplicar resolución del conflicto offline | El propietario eligió `Esta PC` (60 min) y reportó que sincronizó bien. | PASS reportado; revisar los valores finales al ejecutar el tombstone/reset. |
| Reset compartido normal | El propietario informa que el reset de los tres ajustes se propagó correctamente a la otra PC. | PASS reportado; no prueba el conflicto de tombstone. |
| Disponibilidad de la segunda PC | El propietario informa que ya no tiene la PC del hermano. | Las pruebas de tombstone, overrides entre equipos, portátil/peer y preservación comparada quedan en espera; continuar con trabajo local no dependiente del equipo. |
| Preparación del conflicto tombstone | Código Host oculta el preview de reset cuando el snapshot confirmado es un tombstone; el runbook ahora indica recrear un documento activo sincronizado antes de desconectar el propietario. | Requisito previo aclarado; conflicto tombstone aún pendiente en ambas PCs. |
| Paleta Mist/Ocean | Diferente entre equipos. | Fuera del contrato de los tres ajustes globales; almacenamiento local por diseño. |
| Orbes | Propietario reporta iguales. | No prueba sincronización de orb; tampoco forma parte del contrato global. |

No hay aceptación final. Tema, idioma y duración convergen en 30–45 segundos y el reinicio/reapertura conserva sesión/vínculo/consentimiento según reporte. El conflicto offline de duración se resolvió eligiendo Esta PC y el propietario informa que sincronizó correctamente. Faltan conflicto de tombstone/reset, overrides, portátil, preservación de políticas y validación contra staging.

Investigación y candidato correctivo: `docs/progress/20261015-phase3-live-preferences-investigation.md`. La pérdida de sesión queda separada y abierta. Sin cambios en staging ni instalación ejecutada por el agente.
