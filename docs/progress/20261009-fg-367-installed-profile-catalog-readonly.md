# FG-367 — Perfiles y catálogo de aplicaciones instalado .123

## Objetivo

Verificar en el Host instalado que la vista Perfiles representa los nombres guardados y que el catálogo de aplicaciones responde a una búsqueda y vuelve a su lista inicial al limpiar el filtro.

## Prueba y resultado

- El Host .123 mostró cinco perfiles. El seleccionado aparece como `2` porque ese es su nombre guardado; el componente representa `profile.name`, no un identificador. No se renombró el perfil.
- La búsqueda `Adobe` devolvió seis aplicaciones con nombre y ruta exacta desde accesos directos del menú Inicio. Se vació el campo y la primera página volvió a seis aplicaciones.
- El perfil conservó una regla de aplicación y diez reglas web. No se añadieron ni eliminaron reglas y ninguna aplicación fue abierta.
- Diagnostic antes y después permaneció `Expired`, con el mismo SessionId, `Strict=false` y `enforcementConnected=false`.

Evidencia UIA: `artifacts/fg367-output/installed-profile-catalog-readonly.json`.
