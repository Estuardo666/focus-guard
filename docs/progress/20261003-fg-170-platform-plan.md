# FG-170 â€” Fase 2: lÃ­mites de plataforma

Extraer catÃ¡logo, iconos shell/GDI, registro de inicio, supervisor del agente e instancia Ãºnica a FocusGuard.Platform.Windows. Contratos independientes en Client; sÃ³lo conversiÃ³n Bitmap y eventos del framework en Desktop. Mantener nombres de objetos Windows, reintentos, lÃ­mites, HKCU y comportamiento del agente. Biblioteca separada de Infrastructure.Windows para no introducir Client en el servicio de enforcement.

Validar dependencias sin Avalonia, iconos limitados y ausencia de lanzamiento al consultar catÃ¡logo, pruebas existentes de ciclo de vida, soluciÃ³n completa, extensiÃ³n y nuevo instalador 0.1.20. No instalar ni modificar registro, servicios o ACL. La recuperaciÃ³n y el backend no cambian. Trabajar en D NTFS; sincronizar sÃ³lo archivos propios contra hashes previos de E.
