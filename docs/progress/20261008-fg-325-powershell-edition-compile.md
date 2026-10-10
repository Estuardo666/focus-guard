# FG-325 — Compilación del helper de lanzamiento en PowerShell 5.1 y 7

Fecha: 2026-10-08
Estado: corregido y validado bajo Windows PowerShell 5.1 y PowerShell 7; incluido en Setup .110.

## Problema observado

`scripts/test-installer.ps1` compila el helper C# del launcher para ejercitar el dispatcher Explorer. Con la referencia explícita `Microsoft.CSharp`, Windows PowerShell 5.1 pasa, pero PowerShell 7 falla con `CS0656 Missing compiler required member Microsoft.CSharp.RuntimeBinder.Binder.InvokeMember`.

## Plan

1. Añadir `Microsoft.CSharp` explícitamente sólo al compilador Desktop/Windows PowerShell 5.1; dejar que PowerShell 7 use sus referencias runtime predeterminadas.
2. Aplicar la misma selección en el helper que Setup ejecuta y en el test que lo extrae de AST.
3. Ejecutar el test-installer bajo Windows PowerShell 5.1 y PowerShell 7, incluyendo el smoke de dispatcher/owner/integridad.
4. Ejecutar suite .NET/React/extensión, actualizar graphify, regenerar Setup .110 y validar paquete/Host smokes.

## Límite

Esta compatibilidad del helper no otorga token elevado ni instala .110; la aceptación owner-present sigue aparte.

## Implementación y validación

- En `scripts/install-focusguard.ps1` y `scripts/test-installer.ps1`, Desktop/Windows PowerShell agrega referencia `Microsoft.CSharp`; PowerShell Core usa las referencias del runtime.
- El test completo del instalador pasa bajo ambas shells. En ambas, el smoke oculto lanzado por Explorer conserva owner SID/sesión, `elevated=false` y medium integrity.
- Setup .110 incluye el helper. No se modificó el servicio ni se intentó elevar la terminal.
