[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspaceRoot = $PSScriptRoot
$sourcePath = Join-Path $workspaceRoot 'docker-compose.coolify.yml'

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "No existe el compose de Coolify: $sourcePath"
}

$requiredFiles = @(
    'central-api\Dockerfile',
    'central-api\WebApiEndpoints.cs',
    'central-api\DashboardReportService.cs',
    'central-api\WebAuthService.cs',
    'dashboard-web\Dockerfile',
    'dashboard-web\nginx.conf',
    'dashboard-web\package-lock.json'
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $workspaceRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Falta un archivo requerido para desplegar: $relativePath"
    }
}

$compose = Get-Content -LiteralPath $sourcePath -Raw
$requiredMarkers = @(
    'dockerfile: central-api/Dockerfile',
    'dockerfile: dashboard-web/Dockerfile',
    'SERVICE_PASSWORD_64_POSTGRES',
    'restaurant_agent_postgres_data:/var/lib/postgresql'
)

foreach ($marker in $requiredMarkers) {
    if (-not $compose.Contains($marker, [StringComparison]::Ordinal)) {
        throw "El compose no contiene la configuración requerida: $marker"
    }
}

Write-Host 'docker-compose.coolify.yml validado. Coolify construirá API y dashboard desde el repositorio.'
