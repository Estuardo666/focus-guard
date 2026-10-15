# Corrección del ciclo de revalidación de cuenta

Fecha: 2026-10-12. Candidato actual: FocusGuard 0.1.154. Aceptación instalada pendiente.

## Síntoma y causa

Desde la versión .149, la pantalla podía alternar continuamente entre «Comprobando la sesión guardada» y «Comprobando el vínculo de este equipo». `GetSnapshot` usaba cualquier valor del campo compartido `error` como motivo para restaurar OIDC. Un resultado del vínculo o los ajustes (por ejemplo, aprobación necesaria o membresía desactualizada) iniciaba otra restauración aunque la sesión fuera válida. La restauración correcta borraba el aviso y pedía otra comprobación del vínculo; su resultado reiniciaba OIDC. Los controles permanecían deshabilitados durante el ciclo.

La .153 añadió límites y corrigió un caso de cancelación/backoff, pero no separó el motivo del reintento OIDC del aviso de otra operación. El usuario instaló .153 y volvió a observar la alternancia. La comprobación local confirmó que `C:\Program Files\FocusGuard\FocusGuard.UI.dll` coincide exactamente con el payload .153: SHA-256 `8FE6BA99AF3DE8778BDE1D7F756B8D4D1E7E83E626FFFE25DEA576A4203977C9`. Por tanto, la aceptación instalada de .153 falló; no era una versión anterior en ejecución.

## Cambio

- El motivo de reintento se deriva exclusivamente de la fecha establecida por una restauración OIDC fallida, no del aviso compartido de dispositivo o ajustes.
- Un resultado del vínculo permanece visible y no reinicia una sesión OIDC válida. La comprobación periódica conserva su intervalo normal de dos minutos.
- Cerrar sesión elimina cualquier reintento OIDC pendiente.
- La .153 ya había aplicado el tiempo de espera de reintento a la revalidación periódica fallida.
- Cancelar la comprobación de sesión se reporta como conexión no disponible y fija una espera de 30 segundos antes de probar de nuevo.
- Revalidación de sesión y vínculo tienen límites totales de 30 y 45 segundos. El cierre de la aplicación conserva su cancelación normal.
- Un inicio de sesión nuevo y una revalidación correcta limpian la espera anterior.
- La prueba usa un vault sintético y no inicia sesión ni lee el vault personal.

## Evidencia

La regresión inicial de cancelación pasó en .153, pero no cubría el ciclo real de avisos de dispositivo. Cinco casos de esa secuencia fallaron contra .153: después de una restauración correcta, un aviso de vínculo/ajustes provocaba `CheckingSession=true` otra vez.

El candidato .154 pasa nueve pruebas de revalidación: siete resultados ajenos a OIDC permanecen visibles tras diez consultas consecutivas sin restaurar otra vez; cancelación respeta la espera; y un reintento OIDC pendiente no depende de que otro resultado conserve el texto del error. Se usan cuentas/vaults sintéticos; no se leen tokens ni claves personales.

Validación .154: Host 221/221, React 235/235, TypeScript/build de UI, installer script tests, Setup `--package-self-test` exit 0 y paridad Chrome/Edge. ZIP con 1.652 entradas, manifiesto raíz versión .154 y DLL del Host idéntica a la publicación. El primer verificador ad hoc supuso una carpeta contenedora inexistente; se inspeccionó el layout real y la verificación corregida pasó sin alterar el paquete. Graphify actualizado: 19.982 nodos, 100.354 aristas y 1.460 comunidades.

El Host del payload .154 tiene SHA-256 `8339211E0991117A595693E931FB7A358FE73D8A629DC887B47C5AE4EDA28980`, distinto del Host .153 instalado. El build verificó además la DLL y todos los assets del Host embebidos en el Setup.

Setup .154 SHA-256: `46744991E8D93CC2F23E172A6641FF2EB3FF4F7CDA29049147C1B69C3A182821`.

ZIP .154 SHA-256: `4875017D6BAC95C8BCFBA4ECB38BBA200151ED6F393F67CD14751E04C40AE800`.

Setup .153 histórico SHA-256: `E20DAAD5C36F9828AC2E74668099909B9AB995869CC2303ACCC8D9A0E1495B02`.

ZIP .153 histórico SHA-256: `98E7EA6FF5F37DD4DA60A219BFBEA67683BBD0D8E223292C0E451F2FC14D40B0`.

Los hashes históricos de .153 no acreditan resolución del bucle. Falta la aceptación instalada de .154: la tarjeta debe salir de comprobación, conservar el resultado del vínculo y permitir la acción correspondiente. El resultado concreto del vínculo personal se confirmará cuando el ciclo ya no oculte su aviso.

La aceptación de Fase 2 aún requiere dos PC físicos, reset de contraseña de cuenta real, UI QR y vault/interoperabilidad Android. El trabajo Android quedó sin compilar durante esta investigación y no forma parte del instalador Windows.
