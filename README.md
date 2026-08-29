# Piloto de sincronización SoftRestaurant

Implementación mínima del flujo acordado:

```text
SoftRestaurant / SQL Server (solo SELECT)
        -> agente .NET 8 para Windows
        -> cola local SQLite
        -> activación de un solo uso + token por conector
        -> API central .NET 8
        -> PostgreSQL
```

## Carpetas

- `extractor/`: extractor validado, emisor, reintentos y modo Servicio de Windows.
- `sync-contracts/`: contrato JSON compartido entre el agente y la API.
- `central-api/`: API de ingesta, salud, estado de sincronización y resumen diario.
- `docker-compose.yml`: PostgreSQL + API listos para desplegar en Coolify.

## Arranque local de la API

Copiar `.env.example` como `.env`, cambiar las dos contraseñas y ejecutar:

```powershell
docker compose up --build
```

Comprobar `http://localhost:5080/api/health/ready` y después configurar el agente
con la URL HTTPS que Coolify asigne a la API.

## Publicar el agente Windows

```powershell
dotnet publish .\extractor\SoftRestaurant.Extractor.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o .\publish\agent
```

El instalador gráfico solicita una clave de activación de un solo uso. En la primera
conexión el backend entrega una identidad y token exclusivos del equipo; Windows los
guarda cifrados con DPAPI. El agente nunca necesita permisos de escritura en SoftRestaurant.

## Alcance del piloto

Incluye ventas, líneas, pagos, turnos, declaraciones, movimientos de caja,
cancelaciones agregadas y reconciliación. No incluye todavía dashboard visual,
usuarios/roles, catálogo ni actualización automática.
