#Requires -Version 7.2
<#
.SYNOPSIS
Preview a fresh public-demo installation; applying requires its exact reviewed bundle hash.
.DESCRIPTION
Never creates a resource group, registers providers, logs in/out, selects a global account,
publishes code, initializes SQL, or reads an existing catalog. Only a separately authenticated,
explicit nondefault Azure CLI profile is used. Offline compiles/validates without invoking Azure.
The exclusions file is local operator input with resourceGroupIds, resourceNames and identityIds.
See docs/public-demo-deployment.md for prerequisites and the separate schema/content release gate.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][guid] $SubscriptionId,
    [Parameter(Mandatory)][guid] $TenantId,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._()-]{0,89}$')][string] $ResourceGroup,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]{6,14}$')][string] $NameToken,
    [Parameter(Mandatory)][guid] $SqlAdminObjectId,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $SqlAdminLogin,
    [Parameter(Mandatory)][ValidateSet('Group', 'Application')][string] $SqlAdminPrincipalType,
    [Parameter(Mandatory)][string] $ExcludedTargetsPath,
    [Parameter(Mandatory)][string] $ReviewPath,
    [string] $AzureConfigDirectory,
    [switch] $Offline,
    [switch] $Apply,
    [switch] $ApproveCosts,
    [switch] $ApproveRoleAssignments,
    [ValidatePattern('^[a-fA-F0-9]{64}$')][string] $ReviewedSha256,
    [switch] $ProvisionAiModel,
    [switch] $EnablePublicComposer,
    [switch] $EnableMcpServer,
    [switch] $EnablePublicMcp,
    [switch] $EnablePublicPlaygroundWrites,
    [string[]] $PublicMcpAllowedOrigins = @(),
    [ValidateRange(1,100)][int] $PublicComposerDailyRequestLimit = 10,
    [ValidateRange(1,2500000)][int] $PublicComposerDailyTokenLimit = 100000,
    [ValidateRange(1,2)][int] $PublicComposerMaxConcurrentRequests = 1,
    [ValidateRange(1,1000)][int] $PublicComposerMaxGoalCharacters = 1000,
    [ValidateRange(1,16000)][int] $PublicComposerMaxContextBytes = 12000,
    [ValidateRange(1,2048)][int] $PublicComposerMaxOutputTokens = 1024,
    [ValidateRange(1,30)][int] $PublicComposerModelTimeoutSeconds = 20,
    [string] $AiModelName = '',
    [string] $AiModelVersion = '',
    [ValidateSet('DataZoneStandard')][string] $AiModelSku = 'DataZoneStandard',
    [ValidateRange(1,10)][int] $AiModelCapacity = 10
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot -Parent
$subscription = $SubscriptionId.ToString()
$tenant = $TenantId.ToString()
$scope = "/subscriptions/$subscription/resourceGroups/$ResourceGroup"

function Get-Sha256([string] $Text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Assert-Equal($Actual, $Expected, [string] $Label) {
    if ([string]$Actual -ine [string]$Expected) { throw "$Label does not match the explicit reviewed target." }
}

function Invoke-AzJson([string[]] $Arguments) {
    $result = & az @Arguments --subscription $subscription --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Scoped Azure CLI operation failed: $($Arguments[0..1] -join ' ')." }
    if (-not $result) { throw 'Azure CLI returned no authoritative JSON.' }
    return ($result -join "`n" | ConvertFrom-Json -Depth 100)
}

function Assert-FreshTarget {
    $account = Invoke-AzJson @('account', 'show')
    Assert-Equal $account.id $subscription 'Subscription'
    Assert-Equal $account.tenantId $tenant 'Tenant'
    Assert-Equal $account.environmentName 'AzureCloud' 'Azure cloud'
    $group = Invoke-AzJson @('group', 'show', '--name', $ResourceGroup)
    Assert-Equal $group.id $scope 'Resource group'
    Assert-Equal $group.location 'swedencentral' 'Resource group location'
    $inventory = Invoke-AzJson @('resource', 'list', '--resource-group', $ResourceGroup,
        '--query', '{resources: @}')
    if ($null -eq $inventory.PSObject.Properties['resources'] -or
        $inventory.resources -isnot [array] -or $inventory.resources.Count -ne 0) {
        throw 'Public demo requires an authoritatively empty, dedicated resource group. Updates/reuse are not supported.'
    }
    $checks = @(
        @{ Provider = 'Microsoft.Storage'; Api = '2023-05-01'; Name = "stpub$NameToken"; Type = 'Microsoft.Storage/storageAccounts'; Field = 'nameAvailable' },
        @{ Provider = 'Microsoft.Storage'; Api = '2023-05-01'; Name = "stpg$NameToken"; Type = 'Microsoft.Storage/storageAccounts'; Field = 'nameAvailable' },
        @{ Provider = 'Microsoft.Storage'; Api = '2023-05-01'; Name = "stcur$NameToken"; Type = 'Microsoft.Storage/storageAccounts'; Field = 'nameAvailable' },
        @{ Provider = 'Microsoft.Sql'; Api = '2023-08-01'; Name = "sql-public-$NameToken"; Type = 'Microsoft.Sql/servers'; Field = 'available' },
        @{ Provider = 'Microsoft.Web'; Api = '2024-04-01'; Name = "func-public-$NameToken"; Type = 'Microsoft.Web/sites'; Field = 'nameAvailable' }
    )
    if ($ProvisionAiModel) {
        $checks += @{ Provider = 'Microsoft.CognitiveServices'; Api = '2024-10-01'; Name = "ai-public-$NameToken"; Type = 'Microsoft.CognitiveServices/accounts'; Field = 'isSubdomainAvailable' }
    }
    foreach ($check in $checks) {
        $body = @{ name = $check.Name; type = $check.Type } | ConvertTo-Json -Compress
        $path = "/subscriptions/$subscription/providers/$($check.Provider)/checkNameAvailability?api-version=$($check.Api)"
        if ($check.Provider -eq 'Microsoft.CognitiveServices') {
            $body = @{ subdomainName = $check.Name; type = $check.Type; kind = 'AIServices' } | ConvertTo-Json -Compress
            $path = "/subscriptions/$subscription/providers/Microsoft.CognitiveServices/checkDomainAvailability?api-version=$($check.Api)"
        }
        # File payload avoids Windows native-command JSON quote mangling.
        $bodyFile = Join-Path $temp 'name-availability.json'
        [IO.File]::WriteAllText($bodyFile, $body)
        $availability = Invoke-AzJson @('rest', '--method', 'post', '--url', $path, '--body', "@$bodyFile")
        if ($availability.PSObject.Properties[$check.Field].Value -isnot [bool] -or -not $availability.($check.Field)) {
            throw "Name is unavailable or unverified: $($check.Name). Choose a fresh token; never reuse an existing resource."
        }
    }
}

function Get-Preview([string] $TemplateFile, [string] $ParametersFile) {
    $preview = Invoke-AzJson @('deployment', 'group', 'what-if', '--resource-group', $ResourceGroup,
        '--name', "public-demo-$NameToken", '--template-file', $TemplateFile,
        '--parameters', "@$ParametersFile", '--no-pretty-print')
    if ($preview.status -ne 'Succeeded' -or $preview.changes -isnot [array] -or $preview.changes.Count -eq 0) {
        throw 'What-if did not return a successful nonempty authoritative change set.'
    }
    foreach ($change in $preview.changes) {
        if ($change.changeType -notin @('Create', 'NoChange')) {
            throw "Fresh installation preview contains a non-create change: $($change.changeType)."
        }
        if (-not $change.resourceId.StartsWith("$scope/", [StringComparison]::OrdinalIgnoreCase)) {
            throw 'What-if contains a change outside the explicitly chosen resource group.'
        }
    }
    return ($preview.changes | Sort-Object resourceId | ConvertTo-Json -Depth 100 -Compress -AsArray)
}

if ($Offline -and $Apply) { throw 'Offline never applies infrastructure.' }
if ($Apply -and (-not $ApproveCosts -or -not $ApproveRoleAssignments -or -not $ReviewedSha256)) {
    throw 'Apply requires -ApproveCosts, -ApproveRoleAssignments and -ReviewedSha256 from an approved online preview.'
}
foreach ($id in @($SubscriptionId, $TenantId, $SqlAdminObjectId)) {
    if ($id -eq [guid]::Empty) { throw 'Target and SQL administrator GUIDs must be nonempty.' }
}
if ($SqlAdminLogin -match '[\r\n;]' -or $SqlAdminLogin.Length -gt 128) { throw 'Invalid SQL administrator display name.' }
if ($ProvisionAiModel -and ($AiModelName -notmatch '^[a-zA-Z0-9.-]+$' -or $AiModelVersion -notmatch '^[a-zA-Z0-9.-]+$')) {
    throw 'Optional model provisioning requires explicit model name and version; verify current quota/residency/cost before approval.'
}
if ($EnablePublicMcp -and -not $EnableMcpServer) { throw 'Public MCP requires explicit -EnableMcpServer and -EnablePublicMcp.' }
foreach ($origin in $PublicMcpAllowedOrigins) {
    $uri = $null
    if (-not [Uri]::TryCreate($origin, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -cne 'https' -or $uri.UserInfo -or
        $uri.GetLeftPart([UriPartial]::Authority) -cne $origin) {
        throw 'PublicMcpAllowedOrigins must contain exact HTTPS origins without path, credentials or trailing slash.'
    }
}
if ($EnablePublicComposer) {
    if (-not $ProvisionAiModel) { throw 'Public Composer requires explicit -ProvisionAiModel; no existing model may be attached.' }
    foreach ($name in @('PublicComposerDailyRequestLimit', 'PublicComposerDailyTokenLimit',
        'PublicComposerMaxConcurrentRequests', 'PublicComposerMaxGoalCharacters', 'PublicComposerMaxContextBytes',
        'PublicComposerMaxOutputTokens', 'PublicComposerModelTimeoutSeconds')) {
        if (-not $PSBoundParameters.ContainsKey($name)) { throw "Public Composer requires explicit -$name; review every cap." }
    }
    # Same conservative envelope as PublicComposerSettings; the app verifies system-prompt bytes fit 4096.
    $reservationTokens = $PublicComposerMaxContextBytes + 4 * $PublicComposerMaxGoalCharacters + 4096 + 1024 + $PublicComposerMaxOutputTokens
    if ($PublicComposerDailyTokenLimit -lt $reservationTokens) {
        throw "Public Composer daily tokens must cover at least one worst-case reservation ($reservationTokens)."
    }
}
foreach ($path in @($ReviewPath, $ExcludedTargetsPath)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'Review and exclusions paths must be absolute.' }
}
$exclusionsText = Get-Content -LiteralPath $ExcludedTargetsPath -Raw
$exclusions = $exclusionsText | ConvertFrom-Json
foreach ($key in @('resourceGroupIds', 'resourceNames', 'identityIds')) {
    if ($null -eq $exclusions.PSObject.Properties[$key] -or
        $exclusions.$key -isnot [array] -or $exclusions.$key.Count -eq 0) {
        throw "Provide a reviewed nonempty $key exclusion array for the private resources and runtime identities."
    }
    foreach ($value in $exclusions.$key) {
        if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) { throw "Invalid $key exclusion value." }
    }
}
foreach ($id in $exclusions.resourceGroupIds) {
    if ($id -notmatch '^/subscriptions/[0-9a-f-]{36}/resourceGroups/[^/]+$') { throw 'Invalid excluded resource-group ID.' }
}
$deniedIdentities = @()
foreach ($id in $exclusions.identityIds) {
    $parsedId = [guid]::Empty
    if (-not [guid]::TryParse($id, [ref]$parsedId) -or $parsedId -eq [guid]::Empty) { throw 'Invalid excluded runtime identity GUID.' }
    $deniedIdentities += $parsedId.ToString()
}
if ($scope -in $exclusions.resourceGroupIds) {
    throw 'Target overlaps an excluded resource group.'
}
$publicValues = @($ResourceGroup, $NameToken, $SqlAdminObjectId.ToString(), $SqlAdminLogin,
    "func-public-$NameToken", "sql-public-$NameToken", "catalog-public-$NameToken", "stpub$NameToken",
    "id-public-$NameToken-runtime", "id-public-$NameToken-playground-storage",
    "stpg$NameToken", "stcur$NameToken", "id-public-$NameToken-curated-reader")
$denied = @($exclusions.resourceNames) + $deniedIdentities
foreach ($value in $publicValues) {
    if ($value -in $denied) {
        throw 'Target/name/administrator overlaps an excluded resource or identity.'
    }
}

$parameters = [ordered]@{
    nameToken = @{ value = $NameToken }
    tenantId = @{ value = $tenant }
    sqlAdminObjectId = @{ value = $SqlAdminObjectId.ToString() }
    sqlAdminLogin = @{ value = $SqlAdminLogin }
    sqlAdminPrincipalType = @{ value = $SqlAdminPrincipalType }
    provisionAiModel = @{ value = $ProvisionAiModel.IsPresent }
    enablePublicComposer = @{ value = $EnablePublicComposer.IsPresent }
    enableMcpServer = @{ value = $EnableMcpServer.IsPresent }
    enablePublicMcp = @{ value = $EnablePublicMcp.IsPresent }
    enablePublicPlaygroundWrites = @{ value = $EnablePublicPlaygroundWrites.IsPresent }
    publicMcpAllowedOrigins = @{ value = @($PublicMcpAllowedOrigins) }
    publicComposerDailyRequestLimit = @{ value = $PublicComposerDailyRequestLimit }
    publicComposerDailyTokenLimit = @{ value = $PublicComposerDailyTokenLimit }
    publicComposerMaxConcurrentRequests = @{ value = $PublicComposerMaxConcurrentRequests }
    publicComposerMaxGoalCharacters = @{ value = $PublicComposerMaxGoalCharacters }
    publicComposerMaxContextBytes = @{ value = $PublicComposerMaxContextBytes }
    publicComposerMaxOutputTokens = @{ value = $PublicComposerMaxOutputTokens }
    publicComposerModelTimeoutSeconds = @{ value = $PublicComposerModelTimeoutSeconds }
    aiModelName = @{ value = $AiModelName }
    aiModelVersion = @{ value = $AiModelVersion }
    aiModelSku = @{ value = $AiModelSku }
    aiModelCapacity = @{ value = $AiModelCapacity }
}
$parametersJson = [ordered]@{ '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'; contentVersion = '1.0.0.0'; parameters = $parameters } |
    ConvertTo-Json -Depth 20 -Compress
$templateOutput = & bicep build (Join-Path $repoRoot 'infra\public-demo.bicep') --stdout
if ($LASTEXITCODE -ne 0 -or -not $templateOutput) { throw 'Public template compilation failed.' }
$templateJson = $templateOutput -join "`n"
$contract = [ordered]@{
    subscriptionId = $subscription
    tenantId = $tenant
    resourceGroup = $ResourceGroup
    templateSha256 = Get-Sha256 $templateJson
    parametersSha256 = Get-Sha256 $parametersJson
    exclusionsSha256 = Get-Sha256 $exclusionsText
}
$contractJson = $contract | ConvertTo-Json -Compress
if ($Offline) {
    [pscustomobject]@{ kind = 'offline-validation-only'; contract = $contract; parameters = $parameters }
    return
}

if (-not $AzureConfigDirectory -or -not [IO.Path]::IsPathFullyQualified($AzureConfigDirectory) -or
    -not (Test-Path -LiteralPath $AzureConfigDirectory -PathType Container)) {
    throw 'Online preview/apply requires an existing explicit isolated AzureConfigDirectory; authenticate it separately.'
}
$config = (Resolve-Path -LiteralPath $AzureConfigDirectory).Path.TrimEnd('\', '/')
$defaultConfig = [IO.Path]::GetFullPath((Join-Path $HOME '.azure')).TrimEnd('\', '/')
if ($config -ieq $defaultConfig -or (Get-Item -LiteralPath $config).LinkType) {
    throw 'Default/shared or symlinked Azure profile is not allowed.'
}
$review = $null
if ($Apply) {
    $reviewText = Get-Content -LiteralPath $ReviewPath -Raw
    Assert-Equal (Get-Sha256 $reviewText) $ReviewedSha256 'Reviewed bundle hash'
    $review = $reviewText | ConvertFrom-Json -Depth 100
    Assert-Equal $review.kind 'online-public-demo-preview-v1' 'Review kind'
    if (($review.contract | ConvertTo-Json -Compress) -cne $contractJson) {
        throw 'Compiled template/parameters/exclusions contract changed since review.'
    }
} elseif (Test-Path -LiteralPath $ReviewPath) {
    throw 'ReviewPath already exists; choose a new file to preserve the previous review.'
}
$oldConfig = $env:AZURE_CONFIG_DIR
$oldTelemetry = $env:AZURE_CORE_COLLECT_TELEMETRY
$temp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
try {
    $env:AZURE_CONFIG_DIR = $config
    $env:AZURE_CORE_COLLECT_TELEMETRY = 'false'
    $null = New-Item -ItemType Directory -Path $temp
    $templateFile = Join-Path $temp 'public-demo.json'
    $parametersFile = Join-Path $temp 'parameters.json'
    [IO.File]::WriteAllText($templateFile, $templateJson)
    [IO.File]::WriteAllText($parametersFile, $parametersJson)
    Assert-FreshTarget
    $changes = Get-Preview $templateFile $parametersFile
    if ($Apply) {
        if ($changes -cne $review.changes) { throw 'Fresh what-if change set differs from review.' }
        # Recheck freshness immediately before the single apply; never retry failed/partial installs.
        Assert-FreshTarget
        $result = Invoke-AzJson @('deployment', 'group', 'create', '--resource-group', $ResourceGroup,
            '--name', "public-demo-$NameToken", '--mode', 'Incremental',
            '--template-file', $templateFile, '--parameters', "@$parametersFile")
        Assert-Equal $result.properties.provisioningState 'Succeeded' 'Deployment status'
        $result.properties.outputs
    } else {
        $bundle = [ordered]@{ kind = 'online-public-demo-preview-v1'; contract = $contract; changes = $changes }
        $text = $bundle | ConvertTo-Json -Depth 100
        [IO.File]::WriteAllText($ReviewPath, $text)
        Write-Host "Preview only; no resources changed. Review bundle SHA256: $(Get-Sha256 $text)"
        Write-Host 'Apply requires independent target/cost/role/schema review and the exact hash. Nothing has been published.'
    }
} finally {
    $env:AZURE_CONFIG_DIR = $oldConfig
    $env:AZURE_CORE_COLLECT_TELEMETRY = $oldTelemetry
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath (Join-Path $temp 'public-demo.json') -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $temp 'parameters.json') -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $temp 'name-availability.json') -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $temp -Force
    }
}
