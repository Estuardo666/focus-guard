# FG-230 — Alto cliente exacto en compacto plegado

## Cambio

Al entrar a la ventana compacta, `MainForm` mide la diferencia real entre el rectángulo exterior y el cliente y establece el mínimo exterior como 220 DIP de cliente más esa decoración. WinForms ya no limita `ClientSize=480×220` a 480×241.

## Verificación

- Host Windows tests: 149/149; regresión verifica que el mínimo exterior integra el chrome medido.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings/0 errors.
- `scripts/test-installer.ps1`: pasa.
- Setup `--package-self-test`: exit0; package manifest0.1.77 31/31; ZIP de menú1732 entradas.
- Graphify: 10.155 nodos, 27.380 aristas, 997 comunidades.
- Win32 real sobre copia publicada0.1.77, sin `--pilot`: `GetClientRect=480×220`; `GetWindowRect=496×259`. Captura: `D:\TEMP\focusguard-fg166-validation\artifacts\fg230\react-host-0.1.77-compact-collapsed.png`.
- Setup SHA-256 `5129731D5382966ACD11B2C037804BEB8F5323C4501E7CEC1DA310E5A24E6DA1`; ZIP SHA-256 `C8C8241DE2D1F1ED7854366464A4ED8F752B757BA17D7A6D7CE4BD5B917447B8`.

## Límites

El Setup0.1.77 se preservó en `D:\TEMP\focusguard-fg166-validation\artifacts`; no se instaló ni se elevó. El proceso sigue con `IsAdmin=False`. Falta comprobarlo instalado y en distintos DPI/monitores, junto a los gates de Fase9 y F223.
