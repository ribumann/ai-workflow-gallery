// Entry point for the resource-group-scoped AI Workflow Gallery deployment.
// The API runs in a VNet-integrated Function App and reaches data services through private
// endpoints. The Static Web App hosts the frontend and links to the API. Review network policy,
// DNS, and outbound access against the target environment before deployment.
targetScope = 'resourceGroup'

@description('Short application name used to derive resource names (lowercase, no spaces).')
param appName string = 'wfgallery'

@description('Environment name (dev, uat, prod, ...). uat gets seeded mock data for review; prod does not (T060).')
param environmentName string = 'dev'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Azure region for the SQL server/database. Defaults to `location` and can be overridden when regional architecture or service availability requires it.')
param sqlLocation string = location

@description('Display name or UPN of the Microsoft Entra principal to set as the SQL server admin.')
param aadAdminLogin string

@description('Object ID (sid) of the Microsoft Entra principal to set as the SQL server admin.')
param aadAdminObjectId string

@description('Microsoft Entra tenant ID passed to the Function App for tenant-aware role lookup.')
param allowedTenantId string

@description('Allows the Microsoft tenant only when it is also listed in allowedTenantId. Off by default.')
param allowMicrosoftTenant bool = false

@description('Legacy email-domain setting forwarded to Function App configuration; it is not the API authorization boundary.')
param allowedEmailDomain string

@description('Overrides the Azure Communication Services endpoint used by the Feedback Function to send maintainer emails. Leave empty to use the ACS resource provisioned by this template.')
param feedbackEmailEndpoint string = ''

@description('Overrides the ACS Email sender address. Leave empty to use the DoNotReply sender on the Azure managed domain provisioned by this template.')
param feedbackEmailSenderAddress string = ''

@description('Mailbox that receives feedback submissions.')
param feedbackEmailRecipientAddress string = ''

@description('Explicit runtime email opt-in. Resource provisioning and runtime send authorization remain separate prerequisites.')
param enableFeedbackEmail bool = false

@description('Seeds a handful of sample tasks and a linking workflow on Functions cold start. Set true for uat.bicepparam, false for prod.bicepparam (T060).')
param enableDevSeedData bool = false

@description('Enables the AI-assisted workflow composer (issue #59, spec 003): provisions the AI Foundry account/deployment and turns the feature on in the Function App. Set true for uat.bicepparam; kept false for prod.bicepparam until the feature is thoroughly validated in UAT — the module itself still deploys either way, for main.bicep parity/idempotency (T066).')
param enableAiComposer bool = false

@description('Azure region for the AI Foundry account. Independent from `location` and `sqlLocation`.')
param aiFoundryLocation string = 'swedencentral'

@description('Seeds additional sample tasks/workflows for AI composer testing on Functions cold start (UAT only — see Sample Data phase, spec 003).')
param enableComposerSampleData bool = false

@description('Adds five generic, business-productivity sample tasks to the catalog on Functions cold start (idempotent, additive-only — never touches existing real content). Set true for prod.bicepparam so real users have a useful starting catalog; left false for uat.bicepparam, which already gets the broader composer sample set above.')
param enableProdBonusTasks bool = false

@description('Enables the MCP server at /api/mcp (issue #133, spec 007), which exposes the gallery\'s operations to agents behind the same sign-in and tenant gate as the browser. Off by default; set true per environment once validated.')
param enableMcpServer bool = false

@description('Enables MCP write capabilities (spec 007, FR-007) — the twelve create/update/delete tools. When false they are absent from tools/list and rejected on call, so write access can be switched off independently of read access. Off by default; set true per environment once validated.')
param enableMcpWrites bool = false

@description('Number of always-ready Flex Consumption instances for the HTTP trigger group. 1 keeps a single instance warm, removing the ~2.4s host cold start that dominates perceived latency. Defaults to 0 so that an environment which has not opted in — dev, or any environment added later — keeps pure scale-to-zero and is not billed for a permanently running instance; prod and UAT set it explicitly.')
@minValue(0)
@maxValue(10)
param alwaysReadyHttpInstances int = 0

@description('Minutes of inactivity before the serverless SQL database auto-pauses; -1 disables auto-pause. A paused database makes the first request after the idle window pay a multi-second resume, so production disables it while UAT and dev keep pausing to save cost. Azure accepts only -1 or a value of at least 60, hence the allowed list.')
@allowed([
  -1
  60
  120
  240
  360
  720
  1440
])
param sqlAutoPauseDelayMinutes int = 60

@description('Public gallery origin without a trailing slash. When set, it is also used for OAuth resource metadata; when empty, metadata advertises the Function App origin and gallery links infer their origin from request headers.')
param galleryBaseUrl string = ''

var tags = {
  application: appName
  environment: environmentName
  managedBy: 'bicep'
}

var resourceToken = '${appName}-${environmentName}'
var sqlServerName = 'sql-${resourceToken}'
var sqlDatabaseName = 'sqldb-${resourceToken}'
var staticWebAppName = 'swa-${resourceToken}'
var functionAppName = take('func-${resourceToken}', 60)
var entraRegistration = loadJsonContent('./entra/registration.json')
var entraResourceOrigin = empty(galleryBaseUrl) ? 'https://${functionAppName}.azurewebsites.net' : galleryBaseUrl
var entraMcpResource = '${entraResourceOrigin}/api/mcp'
var aiFoundryName = take('aif-${resourceToken}', 63)
var communicationServiceName = take('acs-${resourceToken}', 63)
var emailServiceName = take('acs-email-${resourceToken}', 63)

// Storage Blob Data Contributor — required for the Function App's identity-based AzureWebJobsStorage
// connection and Flex Consumption deployment package access (both use SystemAssignedIdentity auth).
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

// Cognitive Services OpenAI User — data-plane role granting inference (chat/completions) access
// without management/key access, assigned to the Function App's managed identity below (spec 003).
var cognitiveServicesOpenAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

// Communication and Email Service Owner — the data-plane role that lets the Function App's managed
// identity call ACS `emails:send`. ACS has no narrower send-only built-in role today.
var communicationEmailServiceOwnerRoleId = '09976791-48a7-449e-bb21-39d1a415f350'

// Mirrors storage.bicep's own name derivation so the role-assignment `name`/`scope` below can
// reference the storage account without depending on a module output (ARM requires role
// assignment names to be calculable at the start of deployment — see BCP120).
var storageAccountNameComputed = toLower(take('st${replace(resourceToken, '-', '')}${uniqueString(resourceGroup().id)}', 24))

module network 'modules/network.bicep' = {
  name: 'network-deployment'
  params: {
    resourceToken: resourceToken
    location: location
    tags: tags
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql-deployment'
  params: {
    serverName: sqlServerName
    databaseName: sqlDatabaseName
    location: sqlLocation
    tags: tags
    aadAdminLogin: aadAdminLogin
    aadAdminObjectId: aadAdminObjectId
    autoPauseDelayMinutes: sqlAutoPauseDelayMinutes
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage-deployment'
  params: {
    baseName: resourceToken
    location: location
    tags: tags
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name: 'app-insights-deployment'
  params: {
    resourceToken: resourceToken
    location: location
    tags: tags
  }
}

// AI Foundry account for the AI-assisted workflow composer (issue #59, spec 003). Deployed
// unconditionally (mirrors sql.bicep/storage.bicep) so `enableAiComposer` toggling doesn't reshape
// the resource graph — only the Function App setting and RBAC grant below are conditional; the
// account itself always exists, matching T066's "module still deploys, feature just off" decision.
module aiFoundry 'modules/ai-foundry.bicep' = {
  name: 'ai-foundry-deployment'
  params: {
    name: aiFoundryName
    location: aiFoundryLocation
    tags: tags
  }
}

// Azure Communication Services for maintainer feedback email. Deployed unconditionally, like the
// AI Foundry account above: the feedback form is always present in the UI, so the mail path it
// depends on must always exist rather than being a manual, click-ops prerequisite.
module communication 'modules/communication.bicep' = {
  name: 'communication-deployment'
  params: {
    name: communicationServiceName
    emailServiceName: emailServiceName
    tags: tags
  }
}

// Keep private endpoints in their dedicated subnet, separate from Function App integration.
module sqlPrivateEndpoint 'modules/private-endpoint.bicep' = {
  name: 'sql-pe-deployment'
  params: {
    name: 'pe-${sqlServerName}'
    // Private endpoints must live in the VNet's region (`location`), even though the target SQL
    // server itself is in a different region (`sqlLocation`) — cross-region private endpoints are
    // supported, but the endpoint resource always deploys alongside its subnet.
    location: location
    tags: tags
    subnetId: network.outputs.privateEndpointsSubnetId
    privateLinkServiceId: sql.outputs.serverId
    groupId: 'sqlServer'
    // Private DNS zone name is a fixed Azure identifier, not an environment-specific host.
    #disable-next-line no-hardcoded-env-urls
    privateDnsZoneName: 'privatelink.database.windows.net'
    vnetId: network.outputs.vnetId
  }
}

module storagePrivateEndpoint 'modules/private-endpoint.bicep' = {
  name: 'storage-pe-deployment'
  params: {
    name: 'pe-st-${resourceToken}'
    location: location
    tags: tags
    subnetId: network.outputs.privateEndpointsSubnetId
    privateLinkServiceId: storage.outputs.storageAccountId
    groupId: 'blob'
    privateDnsZoneName: 'privatelink.blob.${environment().suffixes.storage}'
    vnetId: network.outputs.vnetId
  }
}

module aiFoundryPrivateEndpoint 'modules/private-endpoint.bicep' = {
  name: 'ai-foundry-pe-deployment'
  params: {
    name: 'pe-${aiFoundryName}'
    location: location
    tags: tags
    subnetId: network.outputs.privateEndpointsSubnetId
    privateLinkServiceId: aiFoundry.outputs.accountId
    groupId: 'account'
    #disable-next-line no-hardcoded-env-urls
    privateDnsZoneName: 'privatelink.cognitiveservices.azure.com'
    vnetId: network.outputs.vnetId
  }
}

module functionApp 'modules/function-app.bicep' = {
  name: 'function-app-deployment'
  params: {
    name: functionAppName
    location: location
    tags: tags
    subnetId: network.outputs.functionsSubnetId
    storageAccountName: storage.outputs.storageAccountName
    storageBlobEndpoint: storage.outputs.blobEndpoint
    deploymentContainerName: storage.outputs.deploymentContainerName
    applicationInsightsConnectionString: appInsights.outputs.connectionString
    alwaysReadyHttpInstances: alwaysReadyHttpInstances
    // The SQL connection string now uses Microsoft Entra Managed Identity authentication (no
    // password) — see sql.bicep. The Function App's own system-assigned identity is the one that
    // authenticates; it is granted access by membership in the SQL AAD admin group (see
    // scripts/deploy-environment.ps1), not a direct database-level grant.
    sqlConnectionString: sql.outputs.connectionString
    storageContainerName: storage.outputs.containerName
    allowedTenantId: allowedTenantId
    entraApiClientId: entraRegistration.apiClientId
    entraRequiredScope: entraRegistration.scope
    allowMicrosoftTenant: allowMicrosoftTenant
    entraMcpResource: entraMcpResource
    allowedEmailDomain: allowedEmailDomain
    feedbackEmailEndpoint: empty(feedbackEmailEndpoint) ? communication.outputs.endpoint : feedbackEmailEndpoint
    feedbackEmailSenderAddress: empty(feedbackEmailSenderAddress) ? communication.outputs.senderAddress : feedbackEmailSenderAddress
    feedbackEmailRecipientAddress: feedbackEmailRecipientAddress
    enableFeedbackEmail: enableFeedbackEmail
    enableDevSeedData: enableDevSeedData
    aiFoundryEndpoint: aiFoundry.outputs.endpoint
    aiFoundryDeploymentName: aiFoundry.outputs.deploymentName
    enableAiComposer: enableAiComposer
    enableComposerSampleData: enableComposerSampleData
    enableProdBonusTasks: enableProdBonusTasks
    enableMcpServer: enableMcpServer
    enableMcpWrites: enableMcpWrites
    galleryBaseUrl: galleryBaseUrl
  }
  dependsOn: [
    storagePrivateEndpoint
    sqlPrivateEndpoint
    aiFoundryPrivateEndpoint
  ]
}

module staticWebApp 'modules/static-web-app.bicep' = {
  name: 'static-web-app-deployment'
  params: {
    name: staticWebAppName
    location: location
    tags: tags
    linkedFunctionAppId: functionApp.outputs.id
    linkedFunctionAppLocation: location
  }
}

// Grants the Function App's system-assigned identity Storage Blob Data Contributor on the storage
// account, required for its identity-based AzureWebJobsStorage connection and Flex Consumption
// deployment package retrieval (both configured with SystemAssignedIdentity auth in function-app.bicep).
resource storageAccountExisting 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountNameComputed
  dependsOn: [
    storage
  ]
}

resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccountNameComputed, functionAppName, storageBlobDataContributorRoleId)
  scope: storageAccountExisting
  properties: {
    principalId: functionApp.outputs.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Grants the Function App's system-assigned identity Cognitive Services OpenAI User on the AI
// Foundry account — data-plane inference access only (no key/management access), and only while
// the composer feature is enabled (T064). The account itself always deploys (see aiFoundry module
// comment); this grant is the one piece of the AI Foundry wiring that stays conditional on
// `enableAiComposer`, keeping prod's Function App identity free of this permission until the
// feature is promoted there.
resource aiFoundryAccountExisting 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: aiFoundryName
  dependsOn: [
    aiFoundry
  ]
}

resource aiFoundryRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableAiComposer) {
  name: guid(aiFoundryName, functionAppName, cognitiveServicesOpenAiUserRoleId)
  scope: aiFoundryAccountExisting
  properties: {
    principalId: functionApp.outputs.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Grants the Function App's system-assigned identity send access on the Communication Services
// resource. Unconditional, unlike the AI Foundry grant above: the feedback form ships enabled in
// every environment, and without this grant the Function returns 502 on every submission.
resource communicationServiceExisting 'Microsoft.Communication/communicationServices@2023-04-01' existing = {
  name: communicationServiceName
  dependsOn: [
    communication
  ]
}

resource communicationRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(communicationServiceName, functionAppName, communicationEmailServiceOwnerRoleId)
  scope: communicationServiceExisting
  properties: {
    principalId: functionApp.outputs.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', communicationEmailServiceOwnerRoleId)
    principalType: 'ServicePrincipal'
  }
}

@description('Default hostname of the deployed Static Web App.')
output staticWebAppHostname string = staticWebApp.outputs.defaultHostname
@description('Name of the Static Web App resource (used by CI/CD deploy steps).')
output staticWebAppName string = staticWebApp.outputs.name

@description('Name of the Function App resource (used by CI/CD deploy steps and scripts/deploy-environment.ps1).')
output functionAppName string = functionApp.outputs.name

@description('Principal ID of the Function App system-assigned identity (used by scripts/deploy-environment.ps1 to add it as a member of the SQL AAD admin group).')
output functionAppPrincipalId string = functionApp.outputs.principalId

@description('Default hostname of the Function App (for direct API smoke tests).')
output functionAppHostname string = functionApp.outputs.defaultHostname

@description('Fully-qualified SQL server hostname.')
output sqlServerFqdn string = sql.outputs.serverFqdn

@description('Name of the storage account holding task files.')
output storageAccountName string = storage.outputs.storageAccountName
