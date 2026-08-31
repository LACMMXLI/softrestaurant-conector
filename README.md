# Piloto de sincronización RestaurantAgent

Implementación mínima del flujo acordado:

```text
RestaurantAgent / SQL Server (solo SELECT)
        -> agente .NET 8 para Windows
        -> cola local SQLite
        -> activación de un solo uso + token por conector
        -> API central .NET 8
        -> PostgreSQL
        -> dashboard web móvil/PWA
```

## Carpetas

- `extractor/`: extractor validado, emisor, reintentos y modo Servicio de Windows.
- `sync-contracts/`: contrato JSON compartido entre el agente y la API.
- `central-api/`: API de ingesta, autenticación y reportes operativos.
- `dashboard-web/`: dashboard React móvil, servido por Nginx como PWA.
- `docker-compose.yml`: PostgreSQL + API + dashboard para ejecución local.
- `docker-compose.coolify.yml`: despliegue desde este repositorio en Coolify.

## Arranque local de la API

Copiar `.env.example` como `.env`, cambiar todas las contraseñas y ejecutar:

```powershell
docker compose up --build
```

Abrir `http://localhost:8080`. El dashboard y la API comparten origen; Nginx reenvía
`/api/*` al contenedor interno. La salud de la API queda en
`http://localhost:5080/api/health/ready`.

Las cuentas se crean y administran desde la aplicación. Las contraseñas se almacenan
únicamente como hash y nunca se devuelven por API.

## Despliegue en Coolify

1. Crear un recurso Docker Compose conectado a este repositorio y seleccionar
   `docker-compose.coolify.yml`.
2. Dejar que Coolify genere `SERVICE_PASSWORD_64_POSTGRES`; no se configuran credenciales de usuarios.
3. Conservar el volumen nombrado `restaurant_agent_postgres_data` en actualizaciones.
4. Asignar el dominio HTTPS al servicio `web`, puerto `8080`. La API no necesita dominio
   público independiente porque el dashboard la publica bajo `/api`.
5. Verificar `/healthz`, iniciar sesión y revisar el indicador de cobertura/frescura antes
   de interpretar cifras.

`generate-coolify-compose.ps1` valida que el compose y los archivos requeridos estén
presentes. Ya no incrusta código en Base64: Coolify construye exactamente el commit del repo.

## Publicar el agente Windows

```powershell
dotnet publish .\extractor\RestaurantAgent.Extractor.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o .\publish\agent
```

El instalador gráfico solicita una clave de activación de un solo uso. En la primera
conexión el backend entrega una identidad y token exclusivos del equipo; Windows los
guarda cifrados con DPAPI. El agente nunca necesita permisos de escritura en RestaurantAgent.

## Alcance actual

Incluye ventas, líneas, pagos, turnos, declaraciones, movimientos de caja,
cancelaciones agregadas, reconciliación, usuarios de dashboard con sesión segura y vistas
móviles de resumen, ventas y operación.

Los totales de ventas solo consideran tickets pagados, no cancelados y con cierre. Cuando
no existe cobertura válida, la interfaz muestra el estado sin sustituirlo por cero. Los IDs
de producto y forma de pago se etiquetan como IDs: el conector todavía no sincroniza los
catálogos para convertirlos en nombres. Inventario y actualización automática del agente
quedan fuera de este alcance.
