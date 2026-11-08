#Requires -Version 7.0
<#
.SYNOPSIS
    End-to-end deployment of one AI Workflow Gallery environment (uat or prod) to Azure.

.DESCRIPTION
    Wraps the full sequence used to deploy or update a gallery environment:

      1. Ensures the target resource group exists.
      2. Ensures the environment's Microsoft Entra security group exists (idempotent) and contains
         the deploying user, then feeds AAD_ADMIN_LOGIN/AAD_ADMIN_OBJECT_ID into the Bicep deployment
         so it becomes the SQL server's Entra admin (see infra/modules/sql.bicep).
      3. Runs `az deployment group create` against infra/main.bicep with the matching
         infra/parameters/<environment>.bicepparam file.
      4. Adds the newly (re)deployed Function App's managed identity to that same Entra group, so it
         gets SQL access purely via Entra group membership — no CREATE USER / direct network path to
         SQL is ever required, which matters once public network access is fully denied.
      5. Publishes API + frontend app content (skip with -SkipAppDeploy for infra-only runs).

    Safe to re-run: every step is idempotent (group creation/membership, Bicep deployment, and the
    two publish steps all tolerate being run against an already-deployed environment).

.PARAMETER Environment
    Which environment to deploy: 'uat' or 'prod'. Selects infra/parameters/<Environment>.bicepparam.

.PARAMETER SubscriptionId
    Azure subscription to deploy into. Required to avoid relying on ambient CLI context.

.PARAMETER AllowedTenantId
    Tenant ID for the directory whose users may access this deployment. Supply the
    value explicitly; it may differ from the Azure subscription tenant.

.PARAMETER AllowedEmailDomain
    Legacy email-domain setting forwarded to Function App configuration. It is
    required for compatibility and is not the API authorization boundary.

.PARAMETER FeedbackEmailEndpoint
    Azure Communication Services endpoint used by the Feedback function to send maintainer email.
    When omitted, main.bicep re-derives it from the ACS module's own output.

.PARAMETER FeedbackEmailSenderAddress
    Verified ACS Email sender address used by the Feedback function. When omitted, main.bicep
    re-derives it from the ACS module's own output.

.PARAMETER FeedbackEmailRecipientAddress
    Mailbox that receives feedback submissions. When omitted, the deployed value is reused; a
    first deployment leaves it empty until an operator configures a recipient.

.PARAMETER SkipAppDeploy
    Deploy infra only; skip building/publishing the API and frontend app content.

.PARAMETER SkipInfraDeploy
    Skip the Bicep deployment; only (re)publish app content. Useful after an infra deploy already
    succeeded and you just changed API/frontend code.

.EXAMPLE
    ./scripts/deploy-environment.ps1 -Environment uat `
        -SubscriptionId '<subscription-guid>' `
        -AllowedTenantId '<tenant-guid>' `
        -AllowedEmailDomain 'example.org'

.EXAMPLE
    ./scripts/deploy-environment.ps1 -Environment prod `
        -SubscriptionId '<subscription-guid>' `
        -AllowedTenantId '<tenant-guid>' `
        -AllowedEmailDomain 'example.org'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('uat', 'prod')]
    [string]$Environment,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$')]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$')]
    [string]$AllowedTenantId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$')]
    [string]$AllowedEmailDomain,

    [switch]$AllowMicrosoftTenant,

    [string]$FeedbackEmailEndpoint,

    [string]$FeedbackEmailSenderAddress,

    [string]$FeedbackEmailRecipientAddress,

    [switch]$SkipAppDeploy,

    [switch]$SkipInfraDeploy
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$origin = git -C $repoRoot remote get-url origin
if ($origin -match 'ribumann/ai-workflow-gallery') {
    throw 'Legacy enterprise deployment is disabled in this checkout. Use only a separately reviewed deployment workflow; no production target is authorized.'
}
$registrationPath = Join-Path $repoRoot 'infra/entra/registration.json'
$registration = Get-Content -Path $registrationPath -Raw | ConvertFrom-Json
if ($registration.spaClientId -notmatch '^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$' -or
    $registration.apiClientId -notmatch '^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$' -or
    $registration.scope -notmatch '^[A-Za-z0-9._-]+$') {
    throw "The SPA/API client IDs and scope in '$registrationPath' must be valid."
}

$appName = 'wfgallery'
$resourceGroupName = "rg-$appName-$Environment"
$deploymentName = "$Environment-deploy-$(Get-Date -Format 'yyyyMMddHHmmss')"
$aadGroupDisplayName = "sg-$appName-sqladmins-$Environment"

function Write-Step {
    param([string]$Message)
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

# --- 0. Subscription context -------------------------------------------------
Write-Step "Setting subscription context"
az account set --subscription $SubscriptionId
if ($LASTEXITCODE -ne 0) {
    throw "Unable to select the requested Azure subscription '$SubscriptionId'."
}
$account = az account show -o json | ConvertFrom-Json
Write-Host "Deploying to subscription '$($account.name)' ($($account.id))"

# --- 1. Resource group --------------------------------------------------------
Write-Step "Ensuring resource group '$resourceGroupName' exists"
$rgLocation = if ($Environment -eq 'prod') { 'westeurope' } else { 'westeurope' }
az group create --name $resourceGroupName --location $rgLocation -o none

# --- 2. Entra admin security group -------------------------------------------
# Only needed for the infra deploy: $group feeds the Bicep SQL admin parameters and receives the
# Function App's managed identity afterwards. An app-content-only run needs none of it, and these
# Graph calls fail outright under tenants that gate `az ad` behind Conditional Access — so don't
# make a code-only redeploy depend on directory access it never uses.
if (-not $SkipInfraDeploy) {
    Write-Step "Ensuring Entra security group '$aadGroupDisplayName' exists"
    $currentUser = az ad signed-in-user show -o json | ConvertFrom-Json
    $group = az ad group list --display-name $aadGroupDisplayName -o json | ConvertFrom-Json | Select-Object -First 1
    if (-not $group) {
        Write-Host "Creating group '$aadGroupDisplayName'..."
        $group = az ad group create --display-name $aadGroupDisplayName --mail-nickname $aadGroupDisplayName -o json | ConvertFrom-Json
    }
    else {
        Write-Host "Group already exists (objectId $($group.id))."
    }

    # Ensure the deploying user is a member + owner so they retain direct SQL access.
    try {
        az ad group member add --group $group.id --member-id $currentUser.id -o none 2>$null
    }
    catch {
        # Tolerate "already a member" — az returns a non-zero exit for a duplicate add.
    }
    try {
        az ad group owner add --group $group.id --owner-object-id $currentUser.id -o none 2>$null
    }
    catch {
        # Tolerate "already an owner".
    }
}

# --- 3. Infra deploy -----------------------------------------------------------
if (-not $SkipInfraDeploy) {
    Write-Step "Deploying infra (Bicep) to '$resourceGroupName'"

    $env:AAD_ADMIN_LOGIN = $aadGroupDisplayName
    $env:AAD_ADMIN_OBJECT_ID = $group.id
    $env:ALLOWED_TENANT_ID = $AllowedTenantId
    $env:ALLOWED_EMAIL_DOMAIN = $AllowedEmailDomain
    $allowMicrosoftTenantValue = $AllowMicrosoftTenant.IsPresent.ToString().ToLowerInvariant()

    # feedbackEmailEndpoint/SenderAddress are self-healing in main.bicep: an empty value resolves to
    # the ACS module's own outputs, so leave them empty and let Bicep re-derive them. Pinning the
    # currently-deployed value here would stop them ever picking up a re-created ACS resource.
    $env:FEEDBACK_EMAIL_ENDPOINT = $FeedbackEmailEndpoint
    $env:FEEDBACK_EMAIL_SENDER_ADDRESS = $FeedbackEmailSenderAddress

    # The recipient has no such fallback — main.bicep passes it straight through. It used to default
    # to a hardcoded mailbox, so a redeploy that omitted the parameter quietly reset a customised
    # recipient back to that address. Reuse whatever is deployed instead.
    $deployedRecipient = ''
    try {
        $existingJson = az functionapp config appsettings list `
            --resource-group $resourceGroupName `
            --name "func-$appName-$Environment" `
            -o json 2>$null
        if ($LASTEXITCODE -eq 0 -and $existingJson) {
            $deployedRecipient = ($existingJson | ConvertFrom-Json |
                Where-Object { $_.name -eq 'FeedbackEmailRecipientAddress' } |
                Select-Object -First 1).value
        }
    }
    catch {
        # First-ever deploy: the Function App doesn't exist yet, so there is nothing to preserve.
    }

    $env:FEEDBACK_EMAIL_RECIPIENT_ADDRESS = if (-not [string]::IsNullOrWhiteSpace($FeedbackEmailRecipientAddress)) {
        $FeedbackEmailRecipientAddress
    }
    elseif (-not [string]::IsNullOrWhiteSpace($deployedRecipient)) {
        Write-Host "Reusing deployed FeedbackEmailRecipientAddress."
        $deployedRecipient
    }
    else { '' }

    $bicepFile = Join-Path $repoRoot 'infra/main.bicep'
    $paramFile = Join-Path $repoRoot "infra/parameters/$Environment.bicepparam"

    az deployment group create `
        --resource-group $resourceGroupName `
        --name $deploymentName `
        --template-file $bicepFile `
        --parameters $paramFile `
        "allowMicrosoftTenant=$allowMicrosoftTenantValue" `
        -o json | Tee-Object -Variable deployResultJson | Out-Null

    $deployResult = $deployResultJson | ConvertFrom-Json
    $functionAppName = $deployResult.properties.outputs.functionAppName.value
    $functionAppPrincipalId = $deployResult.properties.outputs.functionAppPrincipalId.value
    $functionAppHostname = $deployResult.properties.outputs.functionAppHostname.value

    Write-Host "Deployment succeeded. Function App: $functionAppName"

    # --- 4. Add the Function App's managed identity to the Entra admin group ---
    Write-Step "Adding Function App managed identity to '$aadGroupDisplayName'"
    try {
        az ad group member add --group $group.id --member-id $functionAppPrincipalId -o none 2>$null
        Write-Host "Added (or already present)."
    }
    catch {
        Write-Host "Membership add returned a non-zero exit — likely already a member (known AAD read-after-write lag); continuing."
    }
}
else {
    Write-Host "Skipping infra deploy (-SkipInfraDeploy). Looking up existing Function App name..."
    $functionAppName = "func-$appName-$Environment"
}

# --- 5. App content deploy -----------------------------------------------------
if (-not $SkipAppDeploy) {
    Write-Step "Publishing API to '$functionAppName'"
    $npmPrefix = npm config get prefix
    $env:PATH = "$npmPrefix;$env:PATH"

    Push-Location (Join-Path $repoRoot 'api/src')
    try {
        dotnet build --configuration Release
        func azure functionapp publish $functionAppName --dotnet-isolated
    }
    finally {
        Pop-Location
    }

    Write-Step "Building and deploying frontend"
    $swaName = "swa-$appName-$Environment"
    $previousSpaClientId = $env:VITE_ENTRA_CLIENT_ID
    $previousApiClientId = $env:VITE_ENTRA_API_CLIENT_ID
    Push-Location (Join-Path $repoRoot 'frontend')
    try {
        $env:VITE_ENTRA_CLIENT_ID = $registration.spaClientId
        $env:VITE_ENTRA_API_CLIENT_ID = $registration.apiClientId
        npm run build

        # `npm run build` (plain `vite build`) only bundles the SPA itself — it does not know
        # about staticwebapp.config.json (route auth rules, navigationFallback), so it must be
        # copied into the output folder by hand before deploying with the SWA CLI. The official
        # GitHub Action does this automatically via Oryx when app_location is the folder
        # containing this file (see .github/workflows/deploy.yml), but that magic doesn't apply
        # to this manual CLI-based deploy path.
        Copy-Item -Path 'staticwebapp.config.json' -Destination 'dist/staticwebapp.config.json' -Force

        $deployToken = az staticwebapp secrets list -n $swaName -g $resourceGroupName --query "properties.apiKey" -o tsv

        # --swa-config-location must point at dist explicitly: the SWA CLI otherwise resolves
        # staticwebapp.config.json from the app source root (frontend/) — the un-copied original —
        # rather than the one just placed in the deploy artifact above, silently ignoring the copy.
        npx --yes @azure/static-web-apps-cli deploy ./dist --swa-config-location ./dist --deployment-token $deployToken --env production
    }
    finally {
        $env:VITE_ENTRA_CLIENT_ID = $previousSpaClientId
        $env:VITE_ENTRA_API_CLIENT_ID = $previousApiClientId
        Pop-Location
    }
}
else {
    Write-Host "Skipping app content deploy (-SkipAppDeploy)."
}

# --- 6. Summary ------------------------------------------------------------------
Write-Step "Done"
$swaName = "swa-$appName-$Environment"
$swaHostname = az staticwebapp show -n $swaName -g $resourceGroupName --query "defaultHostname" -o tsv
Write-Host "Frontend URL: https://$swaHostname"
Write-Host "Function App: $functionAppName"
if ($group) {
    Write-Host "Entra admin group: $aadGroupDisplayName ($($group.id))"
}
