# FG-302 — Jerarquía UIA del editor de Perfil instalado

Fecha: 2026-10-08
Estado: orden de lectura aceptado parcialmente en Host .100 piloto; captura visual instalada pendiente.

## Prueba
Abrí FocusGuard.UI .100 como usuario normal con --pilot, seleccioné Perfiles por teclado y consulté UI Automation. El árbol WebView expuso153 descendientes y39 elementos focusables. El heading “Reglas guardadas” ocupa Top593; el catálogo aparece bajo “Aplicaciones detectadas” en Top743. Ambos están en el contenido cargado y el orden cumple la jerarquía prevista: reglas del perfil antes del catálogo de aplicaciones. El árbol también expone etiquetas locales para formulario, acciones y ciclo. Los nombres dinámicos de perfil, aplicaciones y sitios no se guardaron.

No se hizo click, no se editó ni guardó perfil; el servicio siguió Ready/Inactive, sin SessionId. El Host se cerró normalmente.

## Evidencia y límite
artifacts/fg298-output/fg301-profile-uia-host100.json. El orden y los límites provienen de AutomationElement bounds, no de una captura de pantalla; por eso no cuentan como aceptación visual. La instalación vigente es .100. El setup .101 todavía requiere acción owner-present. Falta captura de .101 con disclosure colapsado, teclado/foco, forced-colors y prueba DPI.
