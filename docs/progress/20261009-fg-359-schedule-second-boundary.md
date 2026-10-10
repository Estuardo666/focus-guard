# FG-359 — Contrato y borde de 1 segundo para horarios

## Decisión y hallazgo

El propietario confirmó mantener los horarios de 1 minuto. El comportamiento ya implementado permite que el servicio inicie tarde una ocurrencia con el tiempo que queda, conserva el fin programado y omite la ocurrencia cuando queda menos de 1 segundo. `SPEC.md` todavía decía que se necesitaba 1 minuto restante, por lo que la especificación no coincidía con el producto.

## Plan

1. Alinear el contrato y el requisito de UX con la decisión del propietario.
2. Cubrir el límite inclusivo de exactamente 1 segundo restante.
3. Actualizar el estado del plan: .127 sigue siendo candidato; la aceptación instalada no está demostrada.

## Cambio

- `SPEC.md` especifica el mínimo de 1 segundo solo para sesiones iniciadas por horario; las sesiones manuales mantienen su mínimo de 1 minuto.
- `UX_SPEC.md` exige explicar en la revisión final que un inicio tardío acorta la sesión y conserva la hora final.
- `DesktopWorkflowTests` verifica que exactamente 1 segundo restante inicia, conserva el `EndUtc` de la ocurrencia y expira al llegar a ese instante.
- Se corrigió el rótulo contradictorio del plan que ubicaba FG-355 en .125; FG-355 forma parte del candidato .127 vigente.

## Verificación

- Prueba de borde dirigida: 1/1.
- `dotnet test tests/FocusGuard.Service.Tests/FocusGuard.Service.Tests.csproj --configuration Release --no-restore`: 119/119.
- `dotnet format ... --verify-no-changes` no queda limpio: el proyecto ya tiene errores `ENDOFLINE`/`FINALNEWLINE` en archivos ajenos y en secciones preexistentes de `DesktopWorkflowTests.cs`; el nuevo bloque de prueba no aparece entre los errores.
- El cambio solo toca especificaciones, plan, tareas y pruebas; no cambia binarios ni requiere regenerar el instalador.

## Estado instalado y siguiente paso

El servicio instalado actual está ejecutándose como `LocalService` desde `C:\Program Files\FocusGuard`; el Host instalado sigue en .123. El candidato .127 permanece en `D:\TEMP\FocusGuard-0.1.127-fg358-next-end`, con Setup SHA-256 `E66776A179D7FCC39B52F68ECA247F8F529AA335537097C642B8E23035BD4EC7` y ZIP SHA-256 `E98885FCF8F2B71747CD0B525575E2BB7A1A29302B2CCFD56A5F2CE278AC0038`. No instalé el candidato ni cambié horarios o sesiones: el token de esta consola no está elevado. La prueba de fuente no sustituye la aceptación del servicio instalado.

Próxima aceptación: instalar .127 y, desde el Host real, guardar un horario de 1 minuto, comprobar el próximo inicio/fin en la zona del horario, observar el arranque y su expiración en el `EndUtc` original, y retirar el horario temporal. Avalonia permanece disponible; la Fase 10 no se inicia.
