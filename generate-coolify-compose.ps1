[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspaceRoot = $PSScriptRoot
$outputPath = Join-Path $workspaceRoot 'docker-compose.coolify.yml'

$embeddedFiles = @(
    @{ Source = 'central-api\SoftRestaurant.CentralApi.csproj'; Destination = '/src/central-api/SoftRestaurant.CentralApi.csproj' },
    @{ Source = 'central-api\ApiOptions.cs'; Destination = '/src/central-api/ApiOptions.cs' },
    @{ Source = 'central-api\TokenHasher.cs'; Destination = '/src/central-api/TokenHasher.cs' },
    @{ Source = 'central-api\DbInitializer.cs'; Destination = '/src/central-api/DbInitializer.cs' },
    @{ Source = 'central-api\AgentAuthenticator.cs'; Destination = '/src/central-api/AgentAuthenticator.cs' },
    @{ Source = 'central-api\ConnectorRegistry.cs'; Destination = '/src/central-api/ConnectorRegistry.cs' },
    @{ Source = 'central-api\BatchIngestor.cs'; Destination = '/src/central-api/BatchIngestor.cs' },
    @{ Source = 'central-api\Program.cs'; Destination = '/src/central-api/Program.cs' },
    @{ Source = 'central-api\schema.sql'; Destination = '/src/central-api/schema.sql' },
    @{ Source = 'sync-contracts\SoftRestaurant.Sync.Contracts.csproj'; Destination = '/src/sync-contracts/SoftRestaurant.Sync.Contracts.csproj' },
    @{ Source = 'sync-contracts\Contracts.cs'; Destination = '/src/sync-contracts/Contracts.cs' },
    @{ Source = 'sync-contracts\SyncBatch.cs'; Destination = '/src/sync-contracts/SyncBatch.cs' }
)

$lines = [Collections.Generic.List[string]]::new()
@(
    'services:',
    '  postgres:',
    '    image: postgres:18-alpine',
    '    restart: unless-stopped',
    '    environment:',
    '      POSTGRES_DB: softrestaurant_reports',
    '      POSTGRES_USER: softrestaurant',
    '      POSTGRES_PASSWORD: ${SERVICE_PASSWORD_64_POSTGRES}',
    '    volumes:',
    '      - softrestaurant_postgres_data:/var/lib/postgresql',
    '    healthcheck:',
    '      test: ["CMD-SHELL", "pg_isready -U softrestaurant -d softrestaurant_reports"]',
    '      interval: 10s',
    '      timeout: 5s',
    '      retries: 10',
    '',
    '  api:',
    '    build:',
    '      context: .',
    '      dockerfile_inline: |',
    '        FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build',
    '        WORKDIR /src',
    '        RUN mkdir -p /src/central-api /src/sync-contracts'
) | ForEach-Object { $lines.Add($_) }

foreach ($file in $embeddedFiles) {
    $sourcePath = Join-Path $workspaceRoot $file.Source
    $base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($sourcePath))
    $lines.Add("        RUN echo '$base64' | base64 -d > '$($file.Destination)'")
}

@(
    '        RUN dotnet restore /src/central-api/SoftRestaurant.CentralApi.csproj',
    '        RUN dotnet publish /src/central-api/SoftRestaurant.CentralApi.csproj -c Release -o /app/publish --no-restore',
    '        FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime',
    '        WORKDIR /app',
    '        RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*',
    '        COPY --from=build /app/publish .',
    '        ENV ASPNETCORE_URLS=http://+:8080',
    '        EXPOSE 8080',
    '        ENTRYPOINT ["dotnet", "SoftRestaurant.CentralApi.dll"]',
    '    restart: unless-stopped',
    '    environment:',
    '      ASPNETCORE_ENVIRONMENT: Production',
    '      ConnectionStrings__Database: "Host=postgres;Port=5432;Database=softrestaurant_reports;Username=softrestaurant;Password=${SERVICE_PASSWORD_64_POSTGRES}"',
    '      CONNECTOR_ADMIN_KEY: ${SERVICE_PASSWORD_64_CONNECTOR_ADMIN}',
    '      BOOTSTRAP_BRANCH_CODE: sucursal-piloto',
    '      BOOTSTRAP_BRANCH_NAME: Sucursal piloto',
    '      LEGACY_BOOTSTRAP_AGENT_TOKEN: ${SERVICE_PASSWORD_64_AGENT}',
    '    depends_on:',
    '      postgres:',
    '        condition: service_healthy',
    '    expose:',
    '      - "8080"',
    '    healthcheck:',
    '      test: ["CMD-SHELL", "curl --fail --silent http://127.0.0.1:8080/api/health/ready >/dev/null || exit 1"]',
    '      interval: 15s',
    '      timeout: 5s',
    '      retries: 10',
    '',
    'volumes:',
    '  softrestaurant_postgres_data:',
    ''
) | ForEach-Object { $lines.Add($_) }

[IO.File]::WriteAllLines($outputPath, $lines, [Text.UTF8Encoding]::new($false))
Write-Host "Regenerado: $outputPath"
