# Fase 3: hacer visibles los overrides durante conflictos

## Resultado

La confirmación nativa de Windows y la revisión de conflicto del reset mantienen la explicación de que solo cambian los valores compartidos y que los overrides «Kept on this PC / Se conserva en esta PC» siguen activos y tienen prioridad hasta retirarlos. `.181` añade a las revisiones de conflicto normal y de reset una nota con los campos que tienen override local activo. Esto evita confundir una decisión sobre el documento compartido con la retirada de excepciones locales.

## Cambios

- `AccountPanel` muestra los campos overridden como chips de solo lectura si la revisión está en conflicto; la acción para cambiar overrides sigue oculta en ese estado.
- Copia EN/ES añadida para explicar los overrides activos durante la resolución.
- `AccountPanel.test.tsx` comprueba la nota y la lista de campos tanto en conflictos normales como de reset. La confirmación nativa y sus pruebas siguen cubiertas desde `.180`.
- Candidato distribuible `.181`; no se instaló ni desplegó.

## Verificación

- UI: **254/254 PASS** y TypeScript/build PASS.
- Host: **261/261 PASS**.
- Formato y graphify: PASS.
- Guard de producción: PASS.
- `scripts/test-installer.ps1`: PASS (8 casos de readiness y contratos).
- `--package-self-test`: PASS.
- SHA-256 Setup `.181`: `E1932EEFEED0B9EA285511117C8CF7C93C5F35D1D0EA9C85AB616B18C3FDB065`.
- SHA-256 ZIP `.181`: `8D2BB88E5189CA78EDE74DF375A54FF2F68950DEEFA8531959BA61354A566CE6`.

## Límites

La aceptación física de overrides/reset offline en dos PCs y la validación de staging siguen pendientes. Esta aclaración de UX no demuestra recepción física del tombstone ni cambio de valores entre equipos.
