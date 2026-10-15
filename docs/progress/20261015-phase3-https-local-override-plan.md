# Fase 3: precedencia de override local en convergencia HTTPS — plan

## Brecha

La suite cubre overrides locales y convergencia Host→HTTPS por separado. Falta probar que un override de un dispositivo prevalezca sobre una edición remota a través del flujo dual Host, que sobreviva a reabrir desde DPAPI y que al retirarlo se aplique la última versión compartida sin generar una publicación.

## Entrega prevista

Extender la integración HTTPS dual Host existente para:

1. Aplicar un override de tema a un dispositivo sin cambiar la revisión o publicar.
2. Publicar un tema compartido distinto desde el peer y comprobar que el outbox del propietario avanza mientras su preferencia efectiva sigue overridden.
3. Recrear la sesión del propietario desde el vault DPAPI y comprobar que el override y los campos locales sobreviven.
4. Retirar el override; aplicar inmediatamente el último valor global, sin nuevo borrador, revisión ni POST.

Seguridad y recuperación: el override se mantiene en el outbox DPAPI del dispositivo; el transporte y documento compartido solo reflejan el ajuste global; ambos dispositivos siguen sujetos a consentimiento y autorización normales. No cambia producto distribuible ni pretende sustituir aceptación física en dos PCs.

## Aceptación

Una prueba local HTTPS fijada de dos Hosts verifica los pasos anteriores y preserva `ReduceMotion`, `EnforcementEnabled` y notificaciones locales. Luego ejecutar el test focalizado y la suite Host completa, formato y `graphify update .`. No instalar ni desplegar.
