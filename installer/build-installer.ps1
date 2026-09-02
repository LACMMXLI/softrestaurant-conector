[CmdletBinding()]
param(
    [string] $Version = '1.2.0',
    [string] $ApiUrl = 'https://agente-restaurante.fatboymexicali.com',
    [string] $SqlUser = 'sa',
    [string] $SqlPassword = $env:SRX_INSTALLER_SQL_PASSWORD,
    [switch] $TestBuild
)

$ErrorActionPreference = 'Stop'
$installerRoot = $PSScriptRoot
$workspaceRoot = Split-Path -Parent $installerRoot
$publishDirectory = Join-Path $installerRoot '.build\publish'
$distDirectory = Join-Path $installerRoot 'dist'
$configInclude = Join-Path $installerRoot 'build-config.iss'
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

if (-not (Test-Path -LiteralPath $iscc -PathType Leaf)) {
    throw "No se encontró Inno Setup 6 en: $iscc"
}

if ($ApiUrl -notmatch '^https://') {
    throw 'ApiUrl debe usar HTTPS.'
}
if ([string]::IsNullOrWhiteSpace($SqlUser)) {
    throw 'SqlUser no puede estar vacío.'
}

# Si no se proporciona una contraseña durante el build, el instalador la solicita
# en la instalación limpia. Así el artefacto final no depende de secretos ni archivos
# preexistentes y tampoco incrusta una contraseña SQL en el ejecutable.

$buildKind = if ($TestBuild) { 'TEST' } else { 'x64' }
$outputName = "RestaurantAgent-Sync-Agent-$Version-$buildKind"

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null

dotnet publish (Join-Path $workspaceRoot 'extractor\RestaurantAgent.Extractor.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'Falló dotnet publish del agente.'
}

# Panel local (GUI de bandeja): se publica en la misma carpeta que el agente para que
# [Files] del .iss lo tome junto con todo lo demás (copia recursiva de .build\publish\*).
# No requiere administrador para ejecutarse ni toca la configuración protegida del servicio.
dotnet publish (Join-Path $workspaceRoot 'extractor-ui\RestaurantAgent.Extractor.Ui.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'Falló dotnet publish del panel local.'
}

function ConvertTo-InnoLiteral([string] $Value) {
    return $Value.Replace('"', '""')
}

$includeText = @"
#define BuildVersion "$(ConvertTo-InnoLiteral $Version)"
#define BuildApiUrl "$(ConvertTo-InnoLiteral ($ApiUrl.TrimEnd('/')))"
#define BuildSqlUser "$(ConvertTo-InnoLiteral $SqlUser)"
#define BuildSqlPassword "$(ConvertTo-InnoLiteral $SqlPassword)"
#define BuildOutputName "$(ConvertTo-InnoLiteral $outputName)"
"@

try {
    [IO.File]::WriteAllText($configInclude, $includeText, [Text.UTF8Encoding]::new($false))
    & $iscc (Join-Path $installerRoot 'RestaurantAgent.build.iss')
    if ($LASTEXITCODE -ne 0) {
        throw 'Falló la compilación del instalador.'
    }
}
finally {
    if (Test-Path -LiteralPath $configInclude) {
        Remove-Item -LiteralPath $configInclude -Force
    }
}

$artifact = Join-Path $distDirectory "$outputName.exe"
if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
    throw "No se generó el artefacto esperado: $artifact"
}

$file = Get-Item -LiteralPath $artifact
$hash = Get-FileHash -LiteralPath $artifact -Algorithm SHA256
$signature = Get-AuthenticodeSignature -LiteralPath $artifact

[pscustomobject]@{
    Artifact = $file.FullName
    Bytes = $file.Length
    Sha256 = $hash.Hash
    Signature = $signature.Status
    ApiUrl = $ApiUrl.TrimEnd('/')
    PreconfiguredSqlUser = $SqlUser
    TestBuild = [bool]$TestBuild
} | Format-List
