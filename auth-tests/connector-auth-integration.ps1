[CmdletBinding()]
param(
    [string] $ApiUrl = 'https://softrestaurant-api.fatboymexicali.com',
    [Parameter(Mandatory)] [string] $AdminEmail,
    [Parameter(Mandatory)] [securestring] $AdminPassword,
    [string] $BranchCode = "auth-validation-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
)

# Prueba integral del modelo SaaS (Account -> Business -> Branch -> ConnectorInstallation):
# inicia sesión con una cuenta SUPERADMIN (o cualquier cuenta), crea un negocio y una sucursal
# propios, vincula un dispositivo, confirma que un segundo intento de vinculación da 409, prueba
# "reemplazar equipo" (revoca el viejo, emite uno nuevo) y confirma que el token revocado se
# rechaza de inmediato.

$ErrorActionPreference = 'Stop'
$session = $null

function Invoke-TestRequest {
    param(
        [Parameter(Mandatory)] [string] $Method,
        [Parameter(Mandatory)] [string] $Path,
        [hashtable] $Headers = @{},
        [object] $Body,
        [int[]] $ExpectedStatus = @(200)
    )

    $parameters = @{
        Method = $Method
        Uri = "$($ApiUrl.TrimEnd('/'))$Path"
        Headers = $Headers
        SkipHttpErrorCheck = $true
        StatusCodeVariable = 'statusCode'
        WebSession = $session
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ($Body | ConvertTo-Json -Depth 8 -Compress)
    }
    $response = Invoke-WebRequest @parameters
    if ($statusCode -notin $ExpectedStatus) {
        throw "$Method $Path devolvió $statusCode; se esperaba $($ExpectedStatus -join ','). Cuerpo: $($response.Content)"
    }
    if ([string]::IsNullOrWhiteSpace($response.Content)) { return $null }
    return $response.Content | ConvertFrom-Json
}

$plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($AdminPassword))

$loginResponse = Invoke-WebRequest -Method Post -Uri "$($ApiUrl.TrimEnd('/'))/api/web/auth/login" `
    -ContentType 'application/json' -Body (@{ email = $AdminEmail; password = $plainPassword } | ConvertTo-Json) `
    -SessionVariable session
if ($loginResponse.StatusCode -ne 200) { throw "Login falló: $($loginResponse.StatusCode)" }

$business = Invoke-TestRequest -Method Post -Path '/api/web/businesses' -ExpectedStatus 201 `
    -Body @{ name = "Negocio de prueba $BranchCode" }

$branch = Invoke-TestRequest -Method Post -Path "/api/web/businesses/$($business.id)/branches" -ExpectedStatus 201 `
    -Body @{ code = $BranchCode; name = 'Sucursal de prueba' }

$credential = Invoke-TestRequest -Method Post -Path "/api/web/branches/$BranchCode/link-device" `
    -Body @{ machineName = "AUTH-TEST-$([Environment]::MachineName)" }

$conflict = Invoke-TestRequest -Method Post -Path "/api/web/branches/$BranchCode/link-device" -ExpectedStatus 409 `
    -Body @{ machineName = 'SECOND-DEVICE-MUST-CONFLICT' }
if (-not $conflict.activeInstallation -or $conflict.activeInstallation.id -ne $credential.installationId) {
    throw 'El 409 de vinculación duplicada no devolvió la instalación activa esperada.'
}

$connectorHeaders = @{
    'X-Connector-Id' = $credential.installationId
    Authorization = "Bearer $($credential.token)"
}
$null = Invoke-TestRequest -Method Get -Path "/api/branches/$BranchCode/sync-status" -Headers $connectorHeaders

$replacement = Invoke-TestRequest -Method Post -Path "/api/web/branches/$BranchCode/replace-device" `
    -Body @{ machineName = 'SECOND-DEVICE-REPLACEMENT' }
if ($replacement.installationId -eq $credential.installationId) {
    throw 'replace-device debió emitir una instalación nueva, no reutilizar la anterior.'
}

$null = Invoke-TestRequest -Method Get -Path "/api/branches/$BranchCode/sync-status" -Headers $connectorHeaders -ExpectedStatus 401

$newConnectorHeaders = @{
    'X-Connector-Id' = $replacement.installationId
    Authorization = "Bearer $($replacement.token)"
}
$null = Invoke-TestRequest -Method Get -Path "/api/branches/$BranchCode/sync-status" -Headers $newConnectorHeaders

$null = Invoke-TestRequest -Method Post `
    -Path "/api/web/connector-installations/$($replacement.installationId)/revoke?branchCode=$BranchCode"
$null = Invoke-TestRequest -Method Get -Path "/api/branches/$BranchCode/sync-status" -Headers $newConnectorHeaders -ExpectedStatus 401

$history = Invoke-TestRequest -Method Get -Path "/api/web/branches/$BranchCode/connector-installations"
$revokedEntry = $history | Where-Object { $_.id -eq $replacement.installationId }
if (-not $revokedEntry -or $revokedEntry.active -ne $false) {
    throw 'El historial no refleja la revocación esperada.'
}

[pscustomobject]@{
    BranchCode = $BranchCode
    BusinessId = $business.id
    FirstInstallationId = $credential.installationId
    ReplacementInstallationId = $replacement.installationId
    DuplicateLinkConflict = 'OK'
    ReplaceDevice = 'OK'
    OldTokenRejectedAfterReplace = 'OK'
    RevocationRejectedImmediately = 'OK'
} | Format-List
