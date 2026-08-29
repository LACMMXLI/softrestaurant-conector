param(
    [Parameter(Mandatory = $true)] [string] $ExecutablePath,
    [Parameter(Mandatory = $true)] [string] $ApiUrl,
    [Parameter(Mandatory = $true)] [string] $ActivationKey,
    [string] $MachineName = $env:COMPUTERNAME,
    [string] $SqlServer = '.\SQLEXPRESS',
    [string] $SqlDatabase = 'softrestaurant11',
    [string] $SqlUser,
    [string] $SqlPassword,
    [string] $ServiceName = 'SoftRestaurantSyncAgent'
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "El servicio '$ServiceName' ya existe. Desinstálelo antes de volver a instalar."
}

$registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$dataRoot = Join-Path $env:ProgramData 'SoftRestaurantSyncAgent'
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
& "$env:SystemRoot\System32\icacls.exe" $dataRoot /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron proteger los permisos de ProgramData.' }

$plainPath = Join-Path $dataRoot 'agent-settings.tmp.json'
$protectedPath = Join-Path $dataRoot 'agent-settings.dpapi'
$settings = @{
    SRX_API_URL = $ApiUrl
    SRX_ACTIVATION_KEY = $ActivationKey
    SRX_MACHINE_NAME = $MachineName
    SRX_SQL_SERVER = $SqlServer
    SRX_SQL_DATABASE = $SqlDatabase
    SRX_SQL_USER = $SqlUser
    SRX_SQL_PASSWORD = $SqlPassword
    SRX_QUEUE_PATH = (Join-Path $dataRoot 'sync-queue.db')
    SRX_OUTPUT_PATH = (Join-Path $dataRoot 'out')
}
try {
    [IO.File]::WriteAllText($plainPath, ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    & $resolvedExecutable --protect-config $plainPath $protectedPath
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo cifrar la configuración con DPAPI.' }
}
finally {
    [IO.File]::Delete($plainPath)
}

$service = New-Service `
    -Name $ServiceName `
    -DisplayName 'SoftRestaurant Sync Agent' `
    -Description 'Extrae SoftRestaurant en solo lectura y sincroniza con la API central.' `
    -BinaryPathName ('"{0}" --watch' -f $resolvedExecutable) `
    -StartupType Automatic

New-ItemProperty -LiteralPath $registryPath -Name Environment -PropertyType MultiString `
    -Value @("SRX_PROTECTED_CONFIG=$protectedPath") -Force | Out-Null
Start-Service -Name $ServiceName
Get-Service -Name $ServiceName
