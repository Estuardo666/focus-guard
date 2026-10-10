# FG-280 — Eliminar el duplicado del destino en reglas guardadas

## Resultado

En la captura del Host .94 instalado, una regla unresolved (`FocusGuard.TestTarget.exe`) mostraba el mismo texto como nombre y como detalle. `ProfileEditor` ahora oculta el detalle sólo cuando `rule.presentation.name === rule.value`. Cuando difieren, conserva el valor/ruta exactos, por ejemplo bajo un nombre amigable. No se cambia el DTO, la regla persistida, el matching ni la interacción de edición.

Las regresiones cubren ambos casos: la duplicación unresolved fallaba antes del cambio y pasa al ocultar el detalle; la presentación resuelta conserva el destino exacto cuando el nombre presentado difiere. No se editó ni eliminó ninguna regla de los perfiles del usuario.

## Validación

- React UI: 112/112; TypeScript y build de producción pasan.
- Verificación de CSP, recursos locales y contraste pasa.
- Extensión: TypeScript y 19/19 tests.
- Ipc.Windows.Tests: 81/81.
- Los cuatro smokes publicados del Host (`--smoke`, runtime missing, renderer failure y renderer crash) salen con código 0.
- Setup .95 se construyó y verificó: 31/31 archivos del manifest presentes, 18/18 recursos UI iguales al dist y los artefactos Chrome/Edge coinciden con la extensión versionada.

## Paquete y estado instalado

El recibo completo está en `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/build-receipt.json`. Setup `FocusGuard-0.1.95-Setup.exe`: 442845368 bytes, SHA-256 `2BF3383A8FB8BA962DC2B639434E02F3F9A7287E00D110DCC16FFFD17D97EF2F`. ZIP menú: 699156534 bytes, SHA-256 `BE100FE3358E78FF04C4E4F1E8499F5154A4D741428DB812D974DD747D3C22BE`.

.95 no está instalado. La versión vigente es .94, Service PID18792 como LocalService, Ready/Inactive. El Setup solicita elevación y el token disponible para esta sesión no está elevado; no intenté eludir UAC ni iniciar una instalación silenciosa. Los smokes se hicieron contra el publish .95, no contra archivos instalados. La captura `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/installed-profile-0.1.94-keyboard-before.png` documenta el aspecto previo y contiene nombres/reglas del perfil local; mantenerla como evidencia privada.

## Siguiente paso — FG-281

El paquete .95 de este checkpoint fue supersedido antes de instalarse por .96, que conserva este arreglo e incluye copy más clara para el modo de solo lectura. Instalar `D:\TEMP\focusguard-fg166-validation\artifacts\fg280\FocusGuard-0.1.96-Setup.exe` y aprobar su solicitud UAC en el flujo normal. Después abrir la ventana FocusGuard sin elevación y usar Orca para comprobar que `FocusGuard.TestTarget.exe` aparece una sola vez, una regla con nombre amigable aún enseña su ruta/destino exactos y el estado read-only indica cómo seguir. Registrar versión instalada, hashes y captura post-cambio; no iniciar/terminar sesiones ni tocar el perfil.
