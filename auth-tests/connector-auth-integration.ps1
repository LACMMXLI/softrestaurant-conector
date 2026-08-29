[CmdletBinding()]
param(
    [string] $ApiUrl = 'https://softrestaurant-api.fatboymexicali.com',
    [string] $AdminKey = $env:CONNECTOR_ADMIN_KEY,
    [string] $BranchCode = 'auth-validation'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AdminKey)) {
    throw 'Define CONNECTOR_ADMIN_KEY sin imprimirlo en consola.'
}

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
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ($Body | ConvertTo-Json -Depth 8 -Compress)
    }
    $response = Invoke-WebRequest @parameters
    if ($statusCode -notin $ExpectedStatus) {
        throw "$Method $Path devolvió $statusCode; se esperaba $($ExpectedStatus -join ',')."
    }
    if ([string]::IsNullOrWhiteSpace($response.Content)) { return $null }
    return $response.Content | ConvertFrom-Json
}

$adminHeaders = @{ 'X-Admin-Key' = $AdminKey }
$null = Invoke-TestRequest -Method Put -Path "/api/admin/branches/$BranchCode" `
    -Headers $adminHeaders -Body @{ name = 'Validación de autenticación'; timezone = 'America/Tijuana' }

$activation = Invoke-TestRequest -Method Post -Path "/api/admin/branches/$BranchCode/activation-keys" `
    -Headers $adminHeaders -ExpectedStatus 201 `
    -Body @{ expiresInMinutes = 5; note = 'Prueba integral automatizada' }

$connector = Invoke-TestRequest -Method Post -Path '/api/connectors/activate' -Body @{
    activationKey = $activation.activationKey
    machineName = "AUTH-TEST-$([Environment]::MachineName)"
    agentVersion = 'integration-test'
    metadata = @{ purpose = 'connector-auth-integration' }
}

$null = Invoke-TestRequest -Method Post -Path '/api/connectors/activate' -ExpectedStatus 401 -Body @{
    activationKey = $activation.activationKey
    machineName = 'SECOND-USE-MUST-FAIL'
}

$connectorHeaders = @{
    'X-Connector-Id' = $connector.connectorId
    Authorization = "Bearer $($connector.token)"
}
$null = Invoke-TestRequest -Method Get -Path "/api/branches/$BranchCode/sync-status" `
    -Headers $connectorHeaders
$null = Invoke-TestRequest -Method Get -Path '/api/branches/otra-sucursal/sync-status' `
    -Headers $connectorHeaders -ExpectedStatus 401

$rotation = Invoke-TestRequest -Method Post `
    -Path "/api/admin/connectors/$($connector.connectorId)/rotate-token" -Headers $adminHeaders
if ($rotation.connectorId -ne $connector.connectorId -or $rotation.branchCode -ne $BranchCode) {
    throw 'La credencial rotada no identifica correctamente conector y sucursal.'
}
$null = Invoke-TestRequest -Method Get -Path "/api/branches/$BranchCode/sync-status" `
    -Headers $connectorHeaders -ExpectedStatus 401

$connectorHeaders.Authorization = "Bearer $($rotation.token)"
$null = Invoke-TestRequest -Method Get -Path "/api/branches/$BranchCode/sync-status" `
    -Headers $connectorHeaders
$null = Invoke-TestRequest -Method Post `
    -Path "/api/admin/connectors/$($connector.connectorId)/revoke" -Headers $adminHeaders
$null = Invoke-TestRequest -Method Get -Path "/api/branches/$BranchCode/sync-status" `
    -Headers $connectorHeaders -ExpectedStatus 401

$connectors = Invoke-TestRequest -Method Get -Path "/api/admin/branches/$BranchCode/connectors" `
    -Headers $adminHeaders
$validated = $connectors | Where-Object { $_.id -eq $connector.connectorId }
if (-not $validated -or $validated.active -ne $false -or -not $validated.lastSeenAt) {
    throw 'La auditoría final del conector no coincide con revocación/última conexión.'
}

[pscustomobject]@{
    BranchCode = $BranchCode
    ConnectorId = $connector.connectorId
    OneTimeActivation = 'OK'
    BranchBinding = 'OK'
    TokenRotation = 'OK'
    IndividualRevocation = 'OK'
    AuditTrail = 'OK'
} | Format-List
