# DEBUG REPORT — fixture local de identidad no autorizaba sesiones offline

**Síntoma:** `cloud/identity/Test-LocalIdentity.ps1` compilaba y arrancaba Keycloak, pero fallaba en el intercambio correcto de código con `IdentitySession.CompleteAsync`. Antes del fallo, el arnés había aprobado PKCE, rechazo de estado manipulado y consumo del callback.

**Causa raíz:** `IdentitySession` solicita `openid profile email offline_access` y requiere un refresh token para conservar la sesión. El fixture de `Test-LocalIdentity.ps1` importaba un realm sintético sin configurar el scope opcional `offline_access` para el cliente público ni el rol de sesión para el usuario. El log local confirmó `Offline tokens not allowed for the user or client`. El fixture de integración Keycloak/Neon ya aplicaba `Native-SessionPolicy.ps1`; el fixture local no lo hacía.

**Corrección:** `Test-LocalIdentity.ps1` genera credenciales efímeras de administrador de Keycloak, aplica la política existente `Set-FocusGuardNativeSessionPolicy` al realm sintético y restaura las variables de entorno al terminar. No se cambiaron los límites web/API/Android ni se tocó staging.

**Verificación:**

- Antes del cambio, la reproducción falló de forma determinista al intercambiar el código. Log Keycloak: `Offline tokens not allowed for the user or client`.
- Después del cambio, `cloud/identity/Test-LocalIdentity.ps1` pasó **46/46** comprobaciones contra Keycloak 26.8 local y DPAPI Windows, incluyendo intercambio real con PKCE, validación JWT/JWKS, vault DPAPI, restore tras reinicio, rotación de refresh token, tamper, logout/revocación y liberación del callback.
- `FG_API_TEST_CONNECTION` se dejó vacío deliberadamente: esta ejecución local no hizo las pruebas de Neon/API. En ese punto aún no se había repetido el fixture integrado; su resultado posterior se registra abajo.

**Alcance:** el defecto estaba en la configuración del fixture local, no en el flujo de autenticación del producto. El ensayo no usó credenciales personales, Android, cuenta real ni staging.

**Revalidación integrada posterior:** después de que Docker volvió a estar disponible, `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly` pasó **125 checks** con Keycloak/Neon TLS/API/DPAPI. Cubrió el segundo dispositivo, descifrado E2EE, aislamiento, replay, revocación, renovación de sesión y contraseña sintética. También pasó el reinicio del realm. Una ejecución intermedia había recibido `UserInfo invalid_token`, pero la repetición posterior pasó; no se confirmó la causa del fallo no reproducido.

Repetición final con la instrumentación temporal retirada: fixture integrado 125/125 y compilación Identity/API 0 warnings/0 errors. El flujo exitoso quedó verificado con el código de producto restaurado.

**Límites:** la prueba integrada solo usa cuentas y claves sintéticas. No se modificaron credenciales personales ni se desplegó API. El APK se verificó en AVD x86_64, no en un Samsung ARM64.

**Estado:** DONE_WITH_CONCERNS: la causa del fallo del fixture local quedó corregida y verificada, y el fixture integrado pasó; el error transitorio `UserInfo invalid_token` no se reprodujo, por lo que no se le atribuye una causa. Evidencia: `docs/cloud-phase0/PHASE2-COMPLETION-AUDIT.md` y `docs/progress/20261014-phase2-final-check.md`.
