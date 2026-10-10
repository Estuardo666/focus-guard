# FG-308 — Lanzamiento interactivo desde Setup

Fecha: 2026-10-08
Estado: corregido en fuente y empaquetado en .107; aceptación instalada elevada pendiente.

## Evidencia y causa

- El recibo instalado de FG-308 observó `FocusGuard.Desktop` y `FocusGuard.ProcessAgent` con token elevado después de Setup. Al cerrar sólo la UI y volver a abrir Desktop desde el token medio, Desktop y ProcessAgent quedaron ambos con token medio; el servicio siguió como LocalService. Los manifiestos de ambos binarios dicen `asInvoker` y no había `RUNASADMIN`.
- `scripts/install-focusguard.ps1` creaba un objeto nuevo `Shell.Application` dentro del PowerShell elevado y ejecutaba `ShellExecute`. Ese código no identificaba ni llamaba al dispatcher Explorer. Después, las comprobaciones sólo comparaban ruta/nombre y cantidad; nunca contrastaban SID, sesión o integridad, por lo que aceptaban una UI alta.
- Microsoft documenta `ExecInExplorer` precisamente para iniciar procesos sin elevación desde procesos elevados. El código sigue ese modelo: `IShellWindows.FindWindowSW(SWC_DESKTOP)`, ventana del escritorio y `Document.Application.ShellExecute`. Referencias: [muestra ExecInExplorer](https://learn.microsoft.com/en-us/windows/win32/shell/samples-execinexplorer), [IShellWindows](https://learn.microsoft.com/en-us/windows/win32/api/exdisp/nn-exdisp-ishellwindows), [FindWindowSW](https://learn.microsoft.com/en-us/windows/win32/api/exdisp/nf-exdisp-ishellwindows-findwindowsw).
- El helper también consulta token elevation, SID del proceso, integridad y sesión. Compara Desktop y ProcessAgent con el owner configurado, Explorer y el RID medium `8192`; si alguno no coincide, el instalador falla dentro de la transacción y usa el rollback existente.
- La regresión bajo Windows PowerShell 5.1 lanzó un PowerShell oculto de tres segundos a través del dispatcher Explorer. Owner SID y sesión coincidieron; el token fue `elevated=false`, integridad `medium`; el proceso terminó dentro del límite. No se lanzaron binarios de FocusGuard ni se contactó con el Service.
- El token actual de Codex es medio (`AdminToken=False`); por eso no es posible validar aquí el flujo elevado real ni instalar el candidato. No se probará duplicación de tokens, UAC automatizado ni cambio de ACL.

## Implementación y validación

- Setup usa el dispatcher real de Explorer para iniciar Desktop y para el probe IPC de salud. Desktop y ProcessAgent se comprueban por ruta, SID, sesión, token de elevación e integridad media antes de aceptar el update.
- `scripts/test-installer.ps1` compila el helper extraído desde la AST del instalador, valida el dispatcher actual y realiza el proceso de humo oculto. El smoke pasó en Windows PowerShell 5.1, el runtime que usa el Setup.
- Validación: `dotnet test FocusGuard.slnx -c Release --no-restore` pasó 877/877 en 14 assemblies; React 114/114; extensión 19/19; `scripts/test-installer.ps1` y `scripts/test-browser-extension-package.ps1 -Version 0.1.107` pasaron; los cuatro Host smokes locales salieron 0.
- `graphify update .` pasó: 1.238 archivos AST, grafo 10.695 nodos/33.054 aristas. La visualización HTML se omitió por superar el límite configurado.
- Setup .107 se generó en `artifacts/fg308-owner-launch-0.1.107`; manifest 31/31 presentes en árbol y ZIP, UI 18/18 hashes coinciden con `dist`, hash Setup `76EF555B8127EB6D41809DECE0E1FE41695E89E02ADBB0AC053FAD74BC3F1D97`, idéntico dentro del ZIP; hash ZIP `F3CC3C373663AD0A0F1C5808421751C20DBBE3AACC900676431E84BBC08C9DC5`. Recibo: `artifacts/fg308-owner-launch-0.1.107-validation.json`.

## Validación pendiente

El Setup .107 no se instaló; el token de esta consola sigue siendo medio (`AdminToken=False`). La prueba local valida el dispatcher y el proceso que inicia Explorer, pero sólo una actualización owner-present puede confirmar que el Setup elevado deja Desktop y Agent con token medio en esta máquina. Instalación vigente .102 no cambiada; no se cambió Service ni sesión.
