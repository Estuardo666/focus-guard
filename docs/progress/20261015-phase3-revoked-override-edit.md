# Fase 3: editar una excepción local después de revocar sync

## Causa y cambio

La ruta `preferences.set` detectaba que el consentimiento cloud estaba apagado y guardaba correctamente el ajuste local, pero no actualizaba la excepción por campo ya persistida en el outbox DPAPI. Al volver a habilitar sync, la proyección podía reemplazar la nueva preferencia local con el valor viejo de la excepción.

`HostAccountService.TrySyncSharedSettingsEditAsync` ahora actualiza únicamente los campos que ya tenían override local cuando el consentimiento está revocado. Si la sesión de sync sigue viva, usa su outbox. Si fue dispuesta, vuelve a derivar el scope desde el vault vinculado del usuario y abre únicamente el outbox existente bajo la ruta DPAPI/ACL protegida; no crea un baseline implícito.

La actualización usa `SettingsOutboxVault.UpdateAsync` y verifica dentro del lease que el consentimiento siga apagado. No reactiva consentimiento, no lanza el runner y no accede al transporte. Los campos sin override continúan con el comportamiento local normal y no se agregan al documento compartido.

## Regresión

`HostSettingsSyncSessionTests.RevokedSyncPreferenceEditUpdatesOnlyExistingLocalOverridesWithoutNetworkWork`:

1. Activa sync y fija overrides para tema y duración.
2. Desactiva sync y dispone la sesión.
3. Guarda por el bridge un cambio a tema, idioma y duración.
4. Comprueba que preferences.set funciona, el outbox conserva consentimiento revocado, los overrides cambian solo para tema/duración y transporte no recibe lecturas ni escrituras.
5. Recrea la sesión, vuelve a habilitar consentimiento explícitamente y comprueba que la proyección conserva los valores locales recientes.

## Validación

- Prueba focalizada: 1/1 PASS.
- Suite Host Windows completa: 259/259 PASS.
- Suite UI: 254 pruebas en 38 archivos PASS; `npm run check` PASS.
- `scripts/test-installer.ps1`, guard de cliente y Setup `--package-self-test`: PASS.
- Formato whitespace de archivos Host tocados: PASS.
- `graphify update .`: 24.530 nodos, 173.678 relaciones, 1.997 comunidades; HTML omitido por tamaño.
- Setup `.178` SHA-256 `BBA82A9BE973BB63DCEEF467FA1C45169ADE13FE16892AB882BC2F651B6E9CAA`.
- ZIP `.178` SHA-256 `C88F8B796D714665697516831296D0EFC0C222C66CC8DBBB4B02530C8D009E53`.

No se instaló ni desplegó `.178`. La aceptación física entre dos PCs, incluido el conflicto de tombstone, sigue pendiente; el propietario informó que la segunda PC ya no está disponible. El ensayo local no sustituye esa aceptación.
