# Instalador de RestaurantAgent Sync Agent

Este instalador registra el extractor como servicio automático de Windows. Está pensado
para instalarse **una vez por base de datos/sucursal**, normalmente en el equipo que aloja
SQL Server. Si varias cajas comparten la misma base, no debe instalarse en cada caja.

## Flujo de instalación

1. Busca `restaurant.ini` en las rutas conocidas de RestaurantAgent 11, 10 y 9.5.
2. Lee `DataSource` y `Catalog` para completar servidor y base automáticamente.
3. Usa las credenciales SQL preconfiguradas durante la compilación; no vuelve a pedirlas al cliente final.
4. Instala `RestaurantAgentSyncAgent` con inicio automático y recuperación por reinicio.
5. Guarda la cola SQLite y los JSON de diagnóstico en
   `%ProgramData%\RestaurantAgentSyncAgent`.
6. El equipo se vincula desde el panel local usando la cuenta del negocio.
7. Envía por HTTPS y conserva los lotes localmente cuando no hay Internet.

El agente mantiene una lista fija de consultas `SELECT`; usar una cuenta SQL existente con
permisos más amplios no cambia el SQL que ejecuta, aunque sí aumenta el impacto si esa
credencial fuera comprometida. La contraseña y el token se guardan cifrados con DPAPI para
la máquina local en `%ProgramData%\RestaurantAgentSyncAgent\agent-settings.dpapi`. El Registro
del servicio contiene únicamente la ruta de ese archivo.

## Persistencia de la configuración entre actualizaciones (desde 1.2.0)

Toda la configuración específica de un equipo (activación, ID de conector/instalación,
sucursal, servidor/base SQL, usuario y contraseña SQL, token del backend, y la cola SQLite
con el estado de sincronización) vive **fuera** de la carpeta de instalación (`{app}`,
normalmente `Program Files`), en `%ProgramData%\RestaurantAgentSyncAgent`. El instalador solo
copia binarios (`.exe`/`.dll`) dentro de `{app}`; nunca ha tocado `%ProgramData%` al actualizar
archivos, así que esa carpeta ya sobrevivía a un `[Files]` nuevo.

El problema que existía antes de 1.2.0 era otro: el asistente **siempre** pedía servidor, base,
usuario, contraseña y clave de activación, y al terminar la instalación siempre volvía a
cifrar y sobrescribir `agent-settings.dpapi` con esos valores — incluida una clave de
activación de un solo uso ya usada, lo que rompía la reactivación en cada actualización.

Desde 1.2.0, al iniciar el asistente el instalador ejecuta
`RestaurantAgent.Extractor.exe --config-status "<agent-settings.dpapi>"` usando el ejecutable
de la instalación previa (si existe). Si ese comando confirma que ya hay una configuración
completa y descifrable (conexión SQL + token o clave de activación), el instalador:

- Omite por completo las páginas de servidor/base, usuario/contraseña y activación.
- **No** vuelve a generar ni sobrescribir `agent-settings.dpapi`; reutiliza el archivo tal cual está.
- Solo reemplaza los binarios en `{app}` y recrea el servicio de Windows apuntando al mismo
  archivo protegido de siempre.

Si no detecta una configuración previa válida (instalación nueva, o el archivo no se puede
descifrar en este equipo), el asistente pide los datos normalmente, igual que antes.

No hace falta ninguna migración de ubicación: la carpeta de datos siempre fue
`%ProgramData%\RestaurantAgentSyncAgent`, y sigue siéndolo. Un `agent-settings.dpapi` generado
por una instalación 1.1.x ya es compatible con `--config-status` de 1.2.0+.

## Compilar una prueba

```powershell
$env:SRX_INSTALLER_SQL_PASSWORD = '<contraseña de prueba>'
.\installer\build-installer.ps1 -TestBuild
Remove-Item Env:SRX_INSTALLER_SQL_PASSWORD
```

El nombre incluye `TEST`. El instalador no contiene claves de activación ni tokens.

## Generar el instalador final preconfigurado

```powershell
$env:SRX_INSTALLER_SQL_PASSWORD = '<contraseña entregada por National Soft>'
.\installer\build-installer.ps1 -Version 2.1.1 -SqlUser sa
Remove-Item Env:SRX_INSTALLER_SQL_PASSWORD
```

El mismo `.exe` sirve para equipos con la configuración estándar de National Soft. La contraseña
no se guarda en el repositorio ni se imprime en la salida del build, pero sí queda incorporada en
el instalador y después se cifra con DPAPI LocalMachine en el equipo destino. Quien tenga acceso al
instalador podría intentar extraerla; para producción se recomienda reemplazar `sa` por una cuenta
SQL dedicada con permisos únicamente de lectura.

Los artefactos se generan en `installer\dist` y el script imprime tamaño, SHA-256 y estado
de firma.
