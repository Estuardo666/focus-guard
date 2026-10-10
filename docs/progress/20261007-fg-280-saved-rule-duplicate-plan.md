# FG-280 — Plan: quitar repetición en reglas guardadas

## Hallazgo

En la captura real del Host .94 instalado a1216×799, el elemento guardado Block para FocusGuard.TestTarget.exe muestra el mismo texto como nombre principal y otra vez como valor secundario. Ocurre porque ProfileEditor siempre pinta rule.value cuando existe presentation, incluso si presentation.name es exactamente el mismo valor.

## Cambio propuesto

- Mostrar rule.value como detalle sólo si la etiqueta presentada difiere del valor guardado.
- Mantener el path resuelto cuando difiere del valor; no alterar DTO, regla almacenada, matchKind ni mutaciones.
- Añadir pruebas para etiqueta Unresolved igual al valor y etiqueta amigable distinta del valor.

## Aceptación

React tests/check/build y static dist verifier pasan; paquete standalone .95 generado/validado; en Host instalado tras su instalación, el objetivo no aparece duplicado y el valor original sigue visible cuando aporta información. No editar ni quitar reglas del perfil real usado para la captura.