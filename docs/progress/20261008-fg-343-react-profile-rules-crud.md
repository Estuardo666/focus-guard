# FG-343 — Aceptación de perfiles y reglas desde React

**Fecha:** 2026-10-08 local / 2026-10-09 UTC  
**Fase:** 8.1, perfiles y reglas  
**Estado:** aceptado en la UI React .112; el perfil y la regla desechables se eliminaron desde React.

## Plan

1. Verificar Host React .112 en `--pilot`, Service/Extension listos, `SessionState=Stopped`, Strict apagado y conteo inicial de perfiles.
2. Crear desde React un perfil desechable con identificador único.
3. Guardar una regla de sitio Block a través del editor del perfil y confirmar nombre/acción/regla en la UI y en SQLite de sólo lectura.
4. Eliminar únicamente el perfil creado por esta prueba mediante su confirmación de React.
5. Verificar que el perfil desapareció, el conteo volvió al valor inicial y el servicio continúa sin sesión activa.

## Seguridad y recuperación

El perfil tendrá un nombre único `FG343-*` y el destino `.invalid`; ninguna sesión activa aplicará la regla. La eliminación se hará sólo después de verificar que la fila seleccionada tiene ese nombre exacto. Si la limpieza por UI falla, detenerse sin borrar otros perfiles ni modificar SQLite directamente.

## Resultado

El Host React .112 PID29404 estaba en `--pilot`; Service/Extension estaban listos, la sesión era `Stopped` y Strict estaba apagado. El almacén tenía cinco perfiles. React creó `FG343-Probe-00f239c3` (ProfileId `3dddc75c-bddb-4b0f-a2c5-f0f6615d7130`) y guardó la regla web `Block` `fg343-acceptance.invalid` (RuleId `9584ecd4-f9b2-4264-a0eb-522ba88be960`). Una consulta SQLite de sólo lectura confirmó `Action=1` y el conteo pasó a seis.

Después de confirmar en la UI que el diálogo decía «Eliminar perfil · FG343-Probe-00f239c3» y advertía sobre la eliminación de reglas, se confirmó «Eliminar definitivamente». SQLite dejó de encontrar tanto ProfileId como nombre, el conteo regresó a cinco y la UI ya no muestra ese perfil. El Host quedó en `Resumen`; el servicio conserva `Stopped`, Service/Extension listos y Strict apagado.

La prueba escribió el perfil y la regla sólo mediante React; las consultas SQLite fueron read-only. No inició sesión de foco, no navegó al dominio `.invalid` y no cambió los otros cinco perfiles.

## Evidencia y límites

- `artifacts/fg343-output/fg343-profile-created.json`: ID único y conteo 5→6.
- `artifacts/fg343-output/fg343-profile-rule-added.json`: regla Block y RuleId confirmados en SQLite.
- `artifacts/fg343-output/fg343-profile-cleanup.json`: borrado confirmado y conteo 6→5.

Esto acepta una ruta CRUD de perfil + regla desde React en .112. No prueba renombrar/duplicar perfiles, editar reglas existentes, equivalencia Classic/React de todos los campos, horarios asociados, ni el candidato .117.
