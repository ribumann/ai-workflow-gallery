#Requires -Version 7.2
# Real Bicep compilation, isolated CLI doubles. No Azure operations or authentication.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$helper = Join-Path $PSScriptRoot 'deploy-public-demo.ps1'
$template = Join-Path $PSScriptRoot '..\infra\public-demo.bicep'
$compiled = (& bicep build $template --stdout) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Bicep compilation failed.' }
$root = Join-Path ([IO.Path]::GetTempPath()) "gallery-public-tests-$([guid]::NewGuid())"
$null = New-Item -ItemType Directory -Path $root
$profile = Join-Path $root 'isolated-profile'
$null = New-Item -ItemType Directory -Path $profile
$exclusions = Join-Path $root 'exclusions.json'
[IO.File]::WriteAllText($exclusions, '{"resourceGroupIds":["/subscriptions/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/resourceGroups/private-gallery"],"resourceNames":["private-gallery-example","catalog-public-protected"],"identityIds":["{dddddddd-dddd-4ddd-8ddd-dddddddddddd}","eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"]}')
$base = @{
    SubscriptionId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
    TenantId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
    ResourceGroup = 'rg-public-gallery-test'
    NameToken = 'gallerytest'
    SqlAdminObjectId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'
    SqlAdminLogin = 'public-bootstrap-group'
    SqlAdminPrincipalType = 'Group'
    ExcludedTargetsPath = $exclusions
    ReviewPath = Join-Path $root 'review.json'
    AzureConfigDirectory = $profile
}
$scope = "/subscriptions/$($base.SubscriptionId)/resourceGroups/$($base.ResourceGroup)"
$global:publicDeployTest = @{
    calls = @()
    creates = 0
    mode = ''
    compiled = $compiled
    scope = $scope
    subscription = $base.SubscriptionId
    tenant = $base.TenantId
}
$count = 0
function Assert($Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
    $script:count++
}
function bicep {
    $global:LASTEXITCODE = 0
    return $global:publicDeployTest.compiled
}
function az {
    $s = $global:publicDeployTest
    $text = $args -join ' '
    $s.calls += $text
    $global:LASTEXITCODE = 0
    if ($text -notmatch "--subscription $($s.subscription)(?: |$)") { throw 'Unpinned CLI operation.' }
    if ($s.mode -eq 'denied') { $global:LASTEXITCODE = 1; return }
    if ($text -like 'account show *') {
        $result = @{ id = $s.subscription; tenantId = $s.tenant; environmentName = 'AzureCloud' }
        if ($s.mode -eq 'tenant') { $result.tenantId = 'wrong-tenant' }
    } elseif ($text -like 'group show *') {
        $result = @{ id = $s.scope; location = 'swedencentral' }
        if ($s.mode -eq 'region') { $result.location = 'eastus2' }
    } elseif ($text -like 'resource list *') {
        $result = @{ resources = @() }
        if ($s.mode -eq 'reuse') { $result.resources = @(@{ name = 'existing-private-catalog' }) }
        if ($s.mode -eq 'inventory') { $result.resources = $null }
    } elseif ($text -like 'rest *') {
        $available = $s.mode -ne 'name'
        if ($text -match 'Microsoft.Sql/checkNameAvailability') { $result = @{ available = $available } }
        elseif ($text -match 'checkDomainAvailability') {
            $bodyIndex = [array]::IndexOf($args, '--body') + 1
            $body = Get-Content -LiteralPath $args[$bodyIndex].TrimStart('@') -Raw | ConvertFrom-Json
            if ($body.subdomainName -ne 'ai-public-gallerytest') { throw 'Wrong Cognitive Services availability request.' }
            $result = @{ isSubdomainAvailable = $available }
        } else { $result = @{ nameAvailable = $available } }
    } elseif ($text -like 'deployment group what-if *') {
        $id = "$($s.scope)/providers/Microsoft.Web/sites/func-public-gallerytest"
        $change = 'Create'
        if ($s.mode -eq 'modify') { $change = 'Modify' }
        if ($s.mode -eq 'scope') { $id = '/subscriptions/other/resourceGroups/private/providers/Microsoft.Web/sites/private' }
        $result = @{ status = 'Succeeded'; changes = @(@{ resourceId = $id; changeType = $change; after = @{ name = 'public' } }) }
        if ($s.mode -eq 'drift') { $result.changes[0].after.name = 'PUBLIC' }
    } elseif ($text -like 'deployment group create *') {
        $s.creates++
        $result = @{ properties = @{ provisioningState = 'Succeeded'; outputs = @{} } }
    } else { throw "Unexpected CLI operation: $text" }
    return ($result | ConvertTo-Json -Depth 30 -Compress)
}
function Reject([hashtable] $Overrides, [string] $MessagePattern) {
    $p = $base.Clone()
    foreach ($key in $Overrides.Keys) { $p[$key] = $Overrides[$key] }
    $before = $global:publicDeployTest.creates
    try { & $helper @p | Out-Null; throw 'Expected rejection did not occur.' }
    catch {
        Assert ($_.Exception.Message -match $MessagePattern) "Wrong rejection: $($_.Exception.Message)"
        Assert ($before -eq $global:publicDeployTest.creates) 'Rejected path attempted apply.'
    }
}
try {
    $arm = $compiled | ConvertFrom-Json -Depth 100
    Assert ($arm.outputs.runtimeClientId.value -match '\.clientId\]$') 'SQL managed-identity SID input must expose the application/client ID.'
    Assert ($arm.outputs.runtimePrincipalId.value -match '\.principalId\]$') 'ARM RBAC must expose the distinct principal/object ID.'
    $functionDeployment = $arm.resources | Where-Object name -eq 'public-function'
    Assert ($functionDeployment.properties.parameters.storageBlobEndpoint.value -match "reference\(resourceId\('Microsoft.Resources/deployments', 'public-storage'\)") 'Storage endpoint must come from the completed storage module, not an eager existing-account read.'
    $storageDeployment = $arm.resources | Where-Object name -eq 'public-storage'
    Assert ($storageDeployment.properties.template.outputs.blobEndpoint.value -match "primaryEndpoints.blob") 'Storage module must return its actual provisioned endpoint.'
    $offline = & $helper @base -Offline
    Assert ($offline.kind -eq 'offline-validation-only') 'Offline result must not be approval-shaped.'
    Assert ($global:publicDeployTest.calls.Count -eq 0) 'Offline called Azure.'
    Assert (-not (Test-Path -LiteralPath $base.ReviewPath)) 'Offline wrote an online review.'
    Assert ($offline.parameters.enablePublicComposer.value -eq $false) 'Composer must be default-off.'
    Assert ($offline.parameters.enableMcpServer.value -eq $false) 'MCP master must be default-off.'
    Assert ($offline.parameters.enablePublicMcp.value -eq $false) 'Public MCP must be default-off.'
    Assert ($offline.parameters.enablePublicPlaygroundWrites.value -eq $false) 'Playground writes must be default-off.'
    $mcp = & $helper @base -Offline -EnableMcpServer -EnablePublicMcp -PublicMcpAllowedOrigins 'https://gallery.example'
    Assert ($mcp.parameters.enableMcpServer.value -and $mcp.parameters.enablePublicMcp.value) 'Explicit MCP switches were not forwarded.'
    Assert ($mcp.parameters.publicMcpAllowedOrigins.value[0] -ceq 'https://gallery.example') 'Approved browser origin was lost.'
    $playgroundWrites = & $helper @base -Offline -EnablePublicPlaygroundWrites
    Assert ($playgroundWrites.parameters.enablePublicPlaygroundWrites.value) 'Explicit Playground write opt-in was not forwarded.'
    Reject @{ EnablePublicMcp = $true; Offline = $true } 'requires explicit -EnableMcpServer'
    foreach ($origin in @('http://gallery.example', 'https://gallery.example/', 'https://gallery.example/path', 'null', 'https://user:password@gallery.example')) {
        Reject @{ PublicMcpAllowedOrigins = @($origin); Offline = $true } 'exact HTTPS origins'
    }
    $caps = @{
        PublicComposerDailyRequestLimit = 10
        PublicComposerDailyTokenLimit = 100000
        PublicComposerMaxConcurrentRequests = 1
        PublicComposerMaxGoalCharacters = 1000
        PublicComposerMaxContextBytes = 12000
        PublicComposerMaxOutputTokens = 1024
        PublicComposerModelTimeoutSeconds = 20
    }
    $enabled = @{
        EnablePublicComposer = $true; ProvisionAiModel = $true
        AiModelName = 'gpt-5.4-mini'; AiModelVersion = '2026-03-17'; Offline = $true
    }
    foreach ($key in $caps.Keys) { $enabled[$key] = $caps[$key] }
    $onlyModel = & $helper @base -Offline -ProvisionAiModel -AiModelName 'gpt-5.4-mini' -AiModelVersion '2026-03-17'
    Assert ($onlyModel.parameters.enablePublicComposer.value -eq $false) 'Model provisioning alone enabled Composer.'
    $active = & $helper @base @enabled
    Assert ($active.parameters.enablePublicComposer.value -eq $true) 'Explicit Composer opt-in was lost.'
    foreach ($key in $caps.Keys) {
        $armKey = $key.Substring(0, 1).ToLowerInvariant() + $key.Substring(1)
        Assert ($active.parameters[$armKey].value -eq $caps[$key]) "Incorrect enabled $key."
        $missing = $enabled.Clone(); $missing.Remove($key)
        Reject $missing "requires explicit -$key"
    }
    $noModel = $enabled.Clone(); $noModel.ProvisionAiModel = $false
    Reject $noModel 'requires explicit -ProvisionAiModel'
    foreach ($entry in @(
        @('PublicComposerDailyRequestLimit', 101), @('PublicComposerDailyTokenLimit', 2500001),
        @('PublicComposerMaxConcurrentRequests', 3), @('PublicComposerMaxGoalCharacters', 1001),
        @('PublicComposerMaxContextBytes', 16001), @('PublicComposerMaxOutputTokens', 2049),
        @('PublicComposerModelTimeoutSeconds', 31), @('PublicComposerModelTimeoutSeconds', 0)
    )) {
        $invalid = $enabled.Clone(); $invalid[$entry[0]] = $entry[1]
        Reject $invalid $entry[0]
    }
    $underfunded = $enabled.Clone(); $underfunded.PublicComposerDailyTokenLimit = 22143
    Reject $underfunded 'worst-case reservation \(22144\)'
    Assert ($global:publicDeployTest.calls.Count -eq 0) 'Invalid/offline Composer settings called Azure.'
    Reject @{ Apply = $true; Offline = $true } 'Offline never'
    Reject @{ Apply = $true } 'requires'
    Reject @{ SubscriptionId = [guid]::Empty; Offline = $true } 'nonempty'
    Reject @{ ResourceGroup = 'private-gallery'; Offline = $true } 'excluded'
    $protected = (Get-Content -LiteralPath $exclusions -Raw | ConvertFrom-Json).resourceGroupIds[0].Split('/')
    $protectedSubscription = $protected[2]
    $protectedGroup = $protected[4]
    Reject @{ SubscriptionId = $protectedSubscription; ResourceGroup = $protectedGroup; Offline = $true } 'excluded'
    Reject @{ SqlAdminObjectId = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd'; Offline = $true } 'excluded'
    Reject @{ SqlAdminObjectId = 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee'; Offline = $true } 'excluded'
    Reject @{ NameToken = 'protected'; Offline = $true } 'excluded'
    Reject @{ SqlAdminLogin = 'private-gallery-example'; Offline = $true } 'excluded'
    Reject @{ ProvisionAiModel = $true; Offline = $true } 'explicit model'
    Reject @{ AzureConfigDirectory = '' } 'isolated'
    Reject @{ AzureConfigDirectory = (Join-Path $HOME '.azure') } 'Default/shared|isolated'
    foreach ($mode in @('tenant', 'region', 'reuse', 'inventory', 'name', 'denied', 'modify', 'scope')) {
        $global:publicDeployTest.mode = $mode
        Reject @{} 'match|empty|unavailable|failed|non-create|outside'
        Assert (-not (Test-Path -LiteralPath $base.ReviewPath)) "Failed $mode preview wrote a review."
    }
    $global:publicDeployTest.mode = ''
    $sameTenant = $base.Clone()
    $sameTenant.SubscriptionId = 'ffffffff-ffff-4fff-8fff-ffffffffffff'
    $sameTenant.TenantId = '99999999-9999-4999-8999-999999999999'
    $sameTenant.ReviewPath = Join-Path $root 'same-tenant-preview.json'
    $global:publicDeployTest.subscription = $sameTenant.SubscriptionId
    $global:publicDeployTest.tenant = $sameTenant.TenantId
    $global:publicDeployTest.scope = "/subscriptions/$($sameTenant.SubscriptionId)/resourceGroups/$($base.ResourceGroup)"
    & $helper @sameTenant
    Assert (Test-Path -LiteralPath $sameTenant.ReviewPath) 'Explicit shared tenant/subscription with fresh isolated resources was rejected.'
    $global:publicDeployTest.subscription = $base.SubscriptionId
    $global:publicDeployTest.tenant = $base.TenantId
    $global:publicDeployTest.scope = $scope
    $oldConfig = $env:AZURE_CONFIG_DIR
    & $helper @base
    Assert ($env:AZURE_CONFIG_DIR -ceq $oldConfig) 'Helper changed caller Azure profile.'
    Assert ($global:publicDeployTest.creates -eq 0) 'Default preview mutated resources.'
    $reviewText = Get-Content -LiteralPath $base.ReviewPath -Raw
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($reviewText))).ToLowerInvariant()
    $approval = @{ Apply = $true; ApproveCosts = $true; ApproveRoleAssignments = $true; ReviewedSha256 = $hash }
    Reject @{} 'already exists'
    $badHash = $approval.Clone(); $badHash.ReviewedSha256 = '0' * 64
    Reject $badHash 'hash'
    $changed = $approval.Clone(); $changed.NameToken = 'changedtoken'
    Reject $changed 'contract changed'
    $changedCap = $approval.Clone(); $changedCap.PublicComposerDailyTokenLimit = 99999
    Reject $changedCap 'contract changed'
    $changedMcp = $approval.Clone(); $changedMcp.EnableMcpServer = $true; $changedMcp.EnablePublicMcp = $true
    Reject $changedMcp 'contract changed'
    $changedOrigin = $approval.Clone(); $changedOrigin.PublicMcpAllowedOrigins = @('https://gallery.example')
    Reject $changedOrigin 'contract changed'
    $global:publicDeployTest.mode = 'drift'
    Reject $approval 'differs from review'
    $global:publicDeployTest.mode = ''
    & $helper @base @approval | Out-Null
    Assert ($global:publicDeployTest.creates -eq 1) 'Exact approved apply did not execute exactly once.'
    Assert ($env:AZURE_CONFIG_DIR -ceq $oldConfig) 'Apply changed caller Azure profile.'
    $global:publicDeployTest.mode = 'reuse'
    Reject $approval 'empty'
    $global:publicDeployTest.mode = ''
    $model = $base.Clone()
    $model.ReviewPath = Join-Path $root 'model-review.json'
    & $helper @model -ProvisionAiModel -AiModelName 'gpt-5.4-mini' -AiModelVersion '2026-03-17'
    Assert (($global:publicDeployTest.calls -join "`n") -match 'checkDomainAvailability') 'Optional model domain was not checked.'

    $arm = $compiled | ConvertFrom-Json -Depth 100
    $function = ($arm.resources | Where-Object name -eq 'public-function').properties.template
    $app = $function.resources | Where-Object type -eq 'Microsoft.Web/sites'
    Assert ($app.properties.functionAppConfig.runtime.version -eq '10.0') 'Unsupported runtime.'
    Assert ($app.properties.functionAppConfig.deployment.storage.authentication.type -eq 'UserAssignedIdentity') 'Package storage must be keyless.'
    $settings = @{}
    foreach ($setting in $app.properties.siteConfig.appSettings) { $settings[$setting.name] = $setting.value }
    Assert ($settings.PublicDemoEnabled -ceq 'true') 'Public mode opt-in missing.'
    foreach ($name in @('EnableDevSeedData', 'EnableComposerSampleData', 'EnableProdBonusTasks', 'EnableMcpWrites', 'EnableFeedbackEmail')) {
        Assert ($settings[$name] -ceq 'false') "$name must fail closed."
    }
    foreach ($entry in @(@('enableMcpServer', 'EnableMcpServer'), @('enablePublicMcp', 'EnablePublicMcp'))) {
        $parameter = $entry[0]; $setting = $entry[1]
        Assert ($arm.parameters.$parameter.defaultValue -eq $false) "Root $parameter must be default-off."
        Assert ($function.parameters.$parameter.defaultValue -eq $false) "Function $parameter must be default-off."
        Assert ($functionDeployment.properties.parameters.$parameter.value -ceq "[parameters('$parameter')]") "Missing forwarded $parameter."
        Assert ($settings[$setting] -ceq "[if(parameters('$parameter'), 'true', 'false')]") "Incorrect $setting setting."
    }
    Assert ($settings.PublicMcpAllowedOrigins -ceq "[join(parameters('publicMcpAllowedOrigins'), ',')]") 'Origins must be explicit configuration.'
    Assert ($arm.parameters.enablePublicComposer.defaultValue -eq $false) 'Root Composer flag is not default-off.'
    Assert ($function.parameters.enablePublicComposer.defaultValue -eq $false) 'Function Composer flag is not default-off.'
    Assert ($function.variables.composerEnabled -match "parameters\('enablePublicComposer'\).*empty\(parameters\('aiFoundryEndpoint'\)\).*empty\(parameters\('aiFoundryDeploymentName'\)\)") 'Function enablement lacks complete model gating.'
    $forward = ($arm.resources | Where-Object name -eq 'public-function').properties.parameters
    Assert ($forward.enablePublicComposer.value -match "and\(parameters\('enablePublicComposer'\), parameters\('provisionAiModel'\)\)") 'Root flag is not also gated by model provisioning.'
    foreach ($name in @('EnableAiComposer', 'PublicComposerEnabled')) {
        Assert ($settings[$name] -ceq "[if(variables('composerEnabled'), 'true', 'false')]") "$name does not use the single safe opt-in."
    }
    foreach ($name in @('aiFoundryEndpoint', 'aiFoundryDeploymentName')) {
        $appKey = $name.Substring(0, 1).ToUpperInvariant() + $name.Substring(1)
        Assert ($settings[$appKey] -ceq "[parameters('$name')]") "Incorrect $appKey app setting."
        $output = if ($name -eq 'aiFoundryEndpoint') { 'endpoint' } else { 'deploymentName' }
        Assert ($forward.$name -match "if\(parameters\('provisionAiModel'\), createObject\('value', .+outputs.$output.value\), createObject\('value', ''\)\)") "Incorrect conditional $appKey module output."
    }
    foreach ($key in $caps.Keys) {
        $armKey = $key.Substring(0, 1).ToLowerInvariant() + $key.Substring(1)
        $short = $key.Substring('PublicComposer'.Length)
        Assert ($arm.parameters.$armKey.defaultValue -eq $caps[$key]) "Incorrect root default $armKey."
        Assert ($forward.$armKey.value -ceq "[parameters('$armKey')]") "Missing forwarded $armKey."
        Assert ($settings["PublicComposer__$short"] -ceq "[string(parameters('$armKey'))]") "Incorrect canonical app setting $short."
    }
    Assert (-not ($settings.Keys -match '^Entra')) 'Public demo requires dummy Entra app configuration.'
    $storage = ($arm.resources | Where-Object name -eq 'public-storage').properties.template.resources |
        Where-Object type -eq 'Microsoft.Storage/storageAccounts'
    Assert ($storage.properties.publicNetworkAccess -eq 'Disabled') 'Storage is public.'
    Assert ($storage.properties.allowSharedKeyAccess -eq $false) 'Storage allows keys.'
    Assert ($storage.properties.networkAcls.bypass -eq 'None') 'Storage trusted-service bypass enabled.'
    $playgroundStorageModule = $arm.resources | Where-Object name -eq 'playground-storage'
    Assert ($playgroundStorageModule.properties.parameters.name.value -match 'playgroundStorageName') 'Playground storage must use a separate account.'
    Assert ($settings.PlaygroundStorageBlobEndpoint -ceq "[parameters('playgroundStorageBlobEndpoint')]") 'Playground must use its dedicated blob endpoint.'
    Assert ($forward.playgroundStorageBlobEndpoint.value -match 'playground-storage.*outputs.blobEndpoint.value') 'Playground endpoint must come from the separate account module.'
    $sql = ($arm.resources | Where-Object name -eq 'public-sql').properties.template.resources |
        Where-Object type -eq 'Microsoft.Sql/servers'
    Assert ($sql.properties.publicNetworkAccess -eq 'Disabled') 'SQL is public.'
    Assert ($sql.properties.administrators.sid -match 'aadAdminObjectId') 'SQL admin must be separately explicit, not runtime identity.'
    $ai = ($arm.resources | Where-Object name -eq 'public-ai').properties.template.resources |
        Where-Object type -eq 'Microsoft.CognitiveServices/accounts'
    Assert ($ai.properties.publicNetworkAccess -eq 'Disabled') 'Model account is public.'
    Assert ($ai.properties.disableLocalAuth -eq $true) 'Model account permits API keys.'
    $roles = @($arm.resources | Where-Object type -eq 'Microsoft.Authorization/roleAssignments')
    $expectedRoles = @{
        'curated-reader' = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
        'playground-blob-reader' = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
        'playground-blob-writer' = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
        'host-blob-owner' = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
        'openai-user' = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
    }
    Assert ($roles.Count -eq $expectedRoles.Count) 'Unexpected runtime, Playground or curated grant.'
    foreach ($name in $expectedRoles.Keys) {
        $role = @($roles | Where-Object name -match "'$name'\)\]$")
        Assert ($role.Count -eq 1) "Expected exactly one $name grant."
        Assert ($role[0].properties.roleDefinitionId -ceq "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '$($expectedRoles[$name])')]") "Incorrect $name role definition."
    }
    $curatedReader = $roles | Where-Object name -match "'curated-reader'\)\]$"
    Assert ($curatedReader.scope -ceq "[resourceId('Microsoft.Storage/storageAccounts/blobServices/containers', variables('curatedStorageName'), 'default', 'curated-files')]") 'Curated reader must be scoped only to the curated-files container.'
    Assert ($curatedReader.properties.principalId -ceq "[reference(resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', format('id-{0}-curated-reader', variables('token'))), '2023-01-31').principalId]") 'Curated reads must use the dedicated curated-reader identity.'
    Assert (@($roles | Where-Object { $_.properties.principalId -ceq $curatedReader.properties.principalId }).Count -eq 1) 'Curated reader must have no other grants.'
    Assert (@($roles | Where-Object { $_.scope -match 'curatedStorageName|curated-files' -and $_.properties.roleDefinitionId -notmatch '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1' }).Count -eq 0) 'Public worker must not have a curated storage writer or owner.'
    Assert (($roles | Where-Object name -match 'host-blob-owner').scope -match 'Microsoft.Storage/storageAccounts') 'Host role must be storage-account scoped.'
    Assert (($roles | Where-Object name -match 'host-blob-owner').scope -notmatch 'playgroundStorageName') 'Host owner must not cover the Playground account.'
    $inference = $roles | Where-Object name -match 'openai-user'
    Assert ($inference.scope -match 'Microsoft.CognitiveServices/accounts') 'Inference grant must be exact AI-account scoped.'
    Assert ($inference.properties.roleDefinitionId -match '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd') 'Inference role must be OpenAI User.'
    $playgroundReader = $roles | Where-Object name -match "'playground-blob-reader'\)\]$"
    Assert ($playgroundReader.scope -match "Microsoft.Storage/storageAccounts/blobServices/containers'.*'default', 'playground-files'") 'Playground storage reader must be scoped to its private container.'
    Assert ($playgroundReader.properties.principalId -match 'playground-storage') 'Playground storage access must use its dedicated identity.'
    Assert ($playgroundReader.scope -match 'playgroundStorageName') 'Playground reader must target the isolated account.'
    $playgroundWriter = $roles | Where-Object { $_.properties.roleDefinitionId -match 'ba92f5b4-2d11-453d-a403-e96b0029c9fe' }
    Assert ($playgroundWriter.scope -match "Microsoft.Storage/storageAccounts/blobServices/containers'.*'default', 'playground-files'") 'Playground storage writer must be scoped to its private container.'
    Assert ($playgroundWriter.properties.principalId -match 'playground-storage') 'Playground storage writes must use its dedicated identity.'
    Assert ($playgroundWriter.scope -match 'playgroundStorageName') 'Playground writer must target the isolated account.'
    Assert ($playgroundWriter.condition -match 'enablePublicPlaygroundWrites') 'Playground storage writes must require the explicit opt-in.'
    Assert ($compiled -notmatch 'Microsoft.Communication/') 'Mail infrastructure must not be present.'
    Write-Host "PASS: $count public deployment assertions; actual Bicep compilation; no Azure operations."
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force
    Remove-Variable -Name publicDeployTest -Scope Global
}
