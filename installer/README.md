# Instalador de SoftRestaurant Sync Agent

Este instalador registra el extractor como servicio automático de Windows. Está pensado
para instalarse **una vez por base de datos/sucursal**, normalmente en el equipo que aloja
SQL Server. Si varias cajas comparten la misma base, no debe instalarse en cada caja.

## Flujo de instalación

1. Busca `restaurant.ini` en las rutas conocidas de SoftRestaurant 11, 10 y 9.5.
2. Lee `DataSource` y `Catalog` para completar servidor y base automáticamente.
3. Solicita el usuario y contraseña SQL que ya utiliza SoftRestaurant.
4. Solicita una clave de activación de un solo uso y el nombre del equipo.
5. Instala `SoftRestaurantSyncAgent` con inicio automático y recuperación por reinicio.
6. Guarda la cola SQLite y los JSON de diagnóstico en
   `%ProgramData%\SoftRestaurantSyncAgent`.
7. En la primera conexión obtiene un ID/token exclusivo y elimina la clave de activación.
8. Envía por HTTPS y conserva los lotes localmente cuando no hay Internet.

El agente mantiene una lista fija de consultas `SELECT`; usar una cuenta SQL existente con
permisos más amplios no cambia el SQL que ejecuta, aunque sí aumenta el impacto si esa
credencial fuera comprometida. La contraseña y el token se guardan cifrados con DPAPI para
la máquina local en `%ProgramData%\SoftRestaurantSyncAgent\agent-settings.dpapi`. El Registro
del servicio contiene únicamente la ruta de ese archivo.

## Compilar una prueba

```powershell
.\installer\build-installer.ps1 -TestBuild
```

El nombre incluye `TEST`. El instalador no contiene claves de activación ni tokens.

## Generar el instalador final genérico

```powershell
.\installer\build-installer.ps1
```

El mismo `.exe` sirve para cualquier sucursal. La clave de activación se genera en el backend
y se captura durante la instalación; nunca se incorpora un secreto permanente al instalador.

Los artefactos se generan en `installer\dist` y el script imprime tamaño, SHA-256 y estado
de firma.
