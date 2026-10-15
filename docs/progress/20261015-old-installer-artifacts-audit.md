# Auditoría de artefactos viejos del instalador — 15 de octubre de 2026

## Estado actual

Los artefactos `.164` enlazados anteriormente ya no existen bajo `artifacts/installer`; `.165` y `.166` permanecen. En una revalidación separada se encontraron otras 12 carpetas generadas para `.160`–`.163` repartidas en `artifacts/browser-extension`, `artifacts/setup-payload` y `artifacts/setup-publish`.

Las 12 carpetas suman **2.538.513.322 bytes** (aprox. **2,36 GiB**):

| Área | Versión | Bytes |
|---|---:|---:|
| `artifacts/browser-extension` | `.160` | 657.257 |
| `artifacts/browser-extension` | `.161` | 657.255 |
| `artifacts/browser-extension` | `.162` | 653.935 |
| `artifacts/browser-extension` | `.163` | 653.935 |
| `artifacts/setup-payload` | `.160` | 261.757.057 |
| `artifacts/setup-payload` | `.161` | 261.786.731 |
| `artifacts/setup-payload` | `.162` | 253.542.467 |
| `artifacts/setup-payload` | `.163` | 253.542.477 |
| `artifacts/setup-publish` | `.160` | 380.416.672 |
| `artifacts/setup-publish` | `.161` | 380.445.344 |
| `artifacts/setup-publish` | `.162` | 372.200.096 |
| `artifacts/setup-publish` | `.163` | 372.200.096 |

Los doce directorios están bajo `artifacts/`, que `.gitignore` ignora. `scripts/build-installer.ps1` reconstruye esas salidas desde la versión solicitada. `.165` se conserva como evidencia del endpoint Development; `.166` es la versión aceptada en la PC del propietario. No se incluyeron esas versiones retenidas en los objetivos candidatos.

## Resultado de limpieza

Se intentó solo el preflight no destructivo `Remove-Item -WhatIf` sobre las doce rutas absolutas, después de verificar que estaban dentro de la raíz del repositorio y que `.165/.166` existían. La revisión automática lo bloqueó por política antes de ejecutar. No se borró ni movió ningún archivo y no se intentó otra herramienta o ruta para eludir el rechazo.

Esta auditoría distingue dos resultados: los objetivos `.164` ya están ausentes; los artefactos viejos `.160`–`.163` siguen presentes y mantienen pendiente la parte restante de la limpieza. El dueño ya autorizó limpieza cuidadosa, pero la política del entorno impide ejecutarla desde este agente.

## Seguimiento posterior

La siguiente lectura del workspace encontró que las ocho carpetas `setup-payload` y `setup-publish` para `.160`–`.163` ya no existen; las cuatro carpetas pequeñas `browser-extension/0.1.160`–`0.1.163` continúan presentes, con **2.622.382 bytes** combinados. `.165` y `.166` permanecen. D: ahora tiene 119.476.576.256 bytes libres; el agente no ejecutó borrados y no atribuye la desaparición de las ocho carpetas a una acción concreta. El preflight destructivo había sido bloqueado; no se usó otro método para quitar las cuatro restantes.
