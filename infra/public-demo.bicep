// Fresh public-only installation. Never point this template at an existing gallery.
targetScope = 'resourceGroup'

@minLength(6)
@maxLength(14)
@description('Globally unique lowercase alphanumeric public-only name. All resource names are derived from it.')
param nameToken string
param tenantId string
param sqlAdminObjectId string
param sqlAdminLogin string
@allowed(['Group', 'Application'])
param sqlAdminPrincipalType string
@description('Provisions capacity only. This flag alone never enables inference.')
param provisionAiModel bool = false
@description('Explicitly opt into globally budgeted read-only anonymous proposals; also requires model provisioning and SQL ledger grants.')
param enablePublicComposer bool = false
@description('Master MCP switch; public access additionally requires enablePublicMcp. Both default off.')
param enableMcpServer bool = false
param enablePublicMcp bool = false
@description('Enables collection-scoped anonymous writes only after the reviewed SQL policy and storage permissions are applied.')
param enablePublicPlaygroundWrites bool = false
@description('Explicit additional approved HTTPS browser origins. The generated SWA origin is also approved.')
param publicMcpAllowedOrigins array = []
@minValue(1)
@maxValue(100)
param publicComposerDailyRequestLimit int = 10
@minValue(1)
@maxValue(2500000)
param publicComposerDailyTokenLimit int = 100000
@minValue(1)
@maxValue(2)
param publicComposerMaxConcurrentRequests int = 1
@minValue(1)
@maxValue(1000)
param publicComposerMaxGoalCharacters int = 1000
@minValue(1)
@maxValue(16000)
param publicComposerMaxContextBytes int = 12000
@minValue(1)
@maxValue(2048)
param publicComposerMaxOutputTokens int = 1024
@minValue(1)
@maxValue(30)
param publicComposerModelTimeoutSeconds int = 20
param aiModelName string = ''
param aiModelVersion string = ''
@allowed(['DataZoneStandard'])
param aiModelSku string = 'DataZoneStandard'
@minValue(1)
@maxValue(10)
param aiModelCapacity int = 10

var location = 'swedencentral'
var token = 'public-${nameToken}'
var storageName = 'stpub${nameToken}'
var playgroundStorageName = 'stpg${nameToken}'
var curatedStorageName = 'stcur${nameToken}'
var sqlName = 'sql-${token}'
var databaseName = 'catalog-${token}'
var functionName = 'func-${token}'
var aiName = 'ai-${token}'
var tags = {
  application: 'workflow-gallery'
  environment: 'public-demo'
  dataClassification: 'public-reviewed-only'
  managedBy: 'bicep'
}

resource runtime 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${token}-runtime'
  location: location
  tags: tags
}

resource playgroundStorageIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${token}-playground-storage'
  location: location
  tags: tags
}

resource curatedReader 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${token}-curated-reader'
  location: location
  tags: tags
}

module curatedStorage 'modules/public-demo-storage.bicep' = {
  name: 'curated-storage'
  params: {
    name: curatedStorageName
    location: location
    tags: tags
    containerNames: ['curated-files']
  }
}

resource curatedStorageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: curatedStorageName
}
resource curatedBlobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: curatedStorageAccount
  name: 'default'
}
resource curatedFiles 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' existing = {
  parent: curatedBlobService
  name: 'curated-files'
}
resource curatedReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(curatedFiles.id, curatedReader.id, 'curated-reader')
  scope: curatedFiles
  properties: {
    principalId: curatedReader.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1')
  }
  dependsOn: [curatedStorage]
}

module storage 'modules/public-demo-storage.bicep' = {
  name: 'public-storage'
  params: {
    name: storageName
    location: location
    tags: tags
  }
}

module playgroundStorage 'modules/public-demo-storage.bicep' = {
  name: 'playground-storage'
  params: {
    name: playgroundStorageName
    location: location
    tags: tags
    containerNames: ['playground-files']
  }
}

resource playgroundStorageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: playgroundStorageName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: playgroundStorageAccount
  name: 'default'
}

resource playgroundFiles 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' existing = {
  parent: blobService
  name: 'playground-files'
}

resource playgroundStorageReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(playgroundFiles.id, playgroundStorageIdentity.id, 'playground-blob-reader')
  scope: playgroundFiles
  properties: {
    principalId: playgroundStorageIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1')
  }
  dependsOn: [playgroundStorage]
}

resource playgroundStorageWriterRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enablePublicPlaygroundWrites) {
  name: guid(playgroundFiles.id, playgroundStorageIdentity.id, 'playground-blob-writer')
  scope: playgroundFiles
  properties: {
    principalId: playgroundStorageIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
  dependsOn: [playgroundStorage]
}

// Functions host and identity-based package storage require Blob Data Owner.
// No account keys, subscription grants, mail grants or catalog write API are enabled.
resource hostStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, runtime.id, 'host-blob-owner')
  scope: storageAccount
  properties: {
    principalId: runtime.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
  }
  dependsOn: [storage]
}

module network 'modules/network.bicep' = {
  name: 'public-network'
  params: {
    resourceToken: token
    location: location
    tags: tags
    // The helper rejects nonempty groups; these are new subnets, not NSG updates.
    functionsSubnetNetworkSecurityGroupId: null
    privateEndpointsSubnetNetworkSecurityGroupId: null
  }
}

module sql 'modules/sql.bicep' = {
  name: 'public-sql'
  params: {
    serverName: sqlName
    databaseName: databaseName
    location: location
    tags: tags
    aadAdminLogin: sqlAdminLogin
    aadAdminObjectId: sqlAdminObjectId
    aadAdminPrincipalType: sqlAdminPrincipalType
    aadAdminTenantId: tenantId
    maxVCores: 1
    autoPauseDelayMinutes: 60
  }
}

module sqlEndpoint 'modules/private-endpoint.bicep' = {
  name: 'public-sql-endpoint'
  params: {
    name: 'pe-${sqlName}'
    location: location
    tags: tags
    subnetId: network.outputs.privateEndpointsSubnetId
    privateLinkServiceId: sql.outputs.serverId
    groupId: 'sqlServer'
    privateDnsZoneName: 'privatelink${environment().suffixes.sqlServerHostname}'
    vnetId: network.outputs.vnetId
  }
}

module blobEndpoint 'modules/private-endpoint.bicep' = {
  name: 'public-blob-endpoint'
  params: {
    name: 'pe-${storageName}'
    location: location
    tags: tags
    subnetId: network.outputs.privateEndpointsSubnetId
    privateLinkServiceId: storageAccount.id
    groupId: 'blob'
    privateDnsZoneName: 'privatelink.blob.${environment().suffixes.storage}'
    vnetId: network.outputs.vnetId
  }
  dependsOn: [storage]
}

module playgroundBlobEndpoint 'modules/private-endpoint.bicep' = {
  name: 'playground-blob-endpoint'
  params: {
    name: 'pe-${playgroundStorageName}'
    location: location
    tags: tags
    subnetId: network.outputs.privateEndpointsSubnetId
    privateLinkServiceId: playgroundStorageAccount.id
    groupId: 'blob'
    privateDnsZoneName: 'privatelink.blob.${environment().suffixes.storage}'
    vnetId: network.outputs.vnetId
  }
  // Both endpoints share the same DNS zone/link; deploy those resources serially.
  dependsOn: [playgroundStorage, blobEndpoint]
}

module monitoring 'modules/app-insights.bicep' = {
  name: 'public-monitoring'
  params: {
    resourceToken: token
    location: location
    tags: tags
  }
}

module curatedBlobEndpoint 'modules/private-endpoint.bicep' = {
  name: 'curated-blob-endpoint'
  params: {
    name: 'pe-${curatedStorageName}'
    location: location
    tags: tags
    subnetId: network.outputs.privateEndpointsSubnetId
    privateLinkServiceId: curatedStorageAccount.id
    groupId: 'blob'
    privateDnsZoneName: 'privatelink.blob.${environment().suffixes.storage}'
    vnetId: network.outputs.vnetId
  }
  dependsOn: [curatedStorage, playgroundBlobEndpoint]
}

module ai 'modules/ai-foundry.bicep' = if (provisionAiModel) {
  name: 'public-ai'
  params: {
    name: aiName
    location: location
    tags: tags
    modelDeploymentName: aiModelName
    modelName: aiModelName
    modelVersion: aiModelVersion
    modelSku: aiModelSku
    modelCapacity: aiModelCapacity
  }
}

resource aiAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = if (provisionAiModel) {
  name: aiName
}

resource inferenceRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (provisionAiModel) {
  name: guid(aiAccount!.id, runtime.id, 'openai-user')
  scope: aiAccount
  properties: {
    principalId: runtime.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
  }
  dependsOn: [ai]
}

module aiEndpoint 'modules/private-endpoint.bicep' = if (provisionAiModel) {
  name: 'public-ai-endpoint'
  params: {
    name: 'pe-${aiName}'
    location: location
    tags: tags
    subnetId: network.outputs.privateEndpointsSubnetId
    privateLinkServiceId: ai!.outputs.accountId
    groupId: 'account'
    #disable-next-line no-hardcoded-env-urls
    privateDnsZoneName: 'privatelink.cognitiveservices.azure.com'
    vnetId: network.outputs.vnetId
  }
}

resource swa 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'swa-${token}'
  location: 'eastus2'
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    provider: 'None'
    stagingEnvironmentPolicy: 'Disabled'
    allowConfigFileUpdates: true
  }
}

module functionApp 'modules/public-demo-function.bicep' = {
  name: 'public-function'
  params: {
    name: functionName
    location: location
    tags: tags
    subnetId: network.outputs.functionsSubnetId
    runtimeIdentityId: runtime.id
    runtimeClientId: runtime.properties.clientId
    playgroundStorageIdentityId: playgroundStorageIdentity.id
    playgroundStorageClientId: playgroundStorageIdentity.properties.clientId
    curatedReaderIdentityId: curatedReader.id
    curatedReaderClientId: curatedReader.properties.clientId
    curatedStorageBlobEndpoint: curatedStorage.outputs.blobEndpoint
    storageAccountName: storageName
    storageBlobEndpoint: storage.outputs.blobEndpoint
    playgroundStorageBlobEndpoint: playgroundStorage.outputs.blobEndpoint
    sqlConnectionString: '${sql.outputs.connectionString}User Id=${runtime.properties.clientId};'
    applicationInsightsConnectionString: monitoring.outputs.connectionString
    galleryOrigin: 'https://${swa.properties.defaultHostname}'
    enablePublicComposer: enablePublicComposer && provisionAiModel
    enableMcpServer: enableMcpServer
    enablePublicMcp: enablePublicMcp
    enablePublicPlaygroundWrites: enablePublicPlaygroundWrites
    publicMcpAllowedOrigins: union(['https://${swa.properties.defaultHostname}'], publicMcpAllowedOrigins)
    aiFoundryEndpoint: provisionAiModel ? ai!.outputs.endpoint : ''
    aiFoundryDeploymentName: provisionAiModel ? ai!.outputs.deploymentName : ''
    publicComposerDailyRequestLimit: publicComposerDailyRequestLimit
    publicComposerDailyTokenLimit: publicComposerDailyTokenLimit
    publicComposerMaxConcurrentRequests: publicComposerMaxConcurrentRequests
    publicComposerMaxGoalCharacters: publicComposerMaxGoalCharacters
    publicComposerMaxContextBytes: publicComposerMaxContextBytes
    publicComposerMaxOutputTokens: publicComposerMaxOutputTokens
    publicComposerModelTimeoutSeconds: publicComposerModelTimeoutSeconds
  }
  dependsOn: [hostStorageRole, sqlEndpoint, curatedBlobEndpoint, curatedReaderRole, playgroundStorageReaderRole, playgroundStorageWriterRole]
}

resource linkedBackend 'Microsoft.Web/staticSites/linkedBackends@2023-12-01' = {
  parent: swa
  name: 'api'
  properties: {
    backendResourceId: functionApp.outputs.id
    region: location
  }
}

// Worker public-mode middleware, not SWA headers, is the read-only collection boundary.
module workerAuth 'modules/public-demo-worker-auth.bicep' = {
  name: 'public-worker-auth'
  params: {
    functionAppName: functionName
  }
  dependsOn: [linkedBackend]
}

output functionAppName string = functionName
output galleryOrigin string = 'https://${swa.properties.defaultHostname}'
output sqlServerName string = sqlName
output databaseName string = databaseName
@description('Object ID for ARM RBAC assignments; not the SID of a contained SQL managed-identity user.')
output runtimePrincipalId string = runtime.properties.principalId
@description('Application/client ID; convert to binary(16) for a contained SQL managed-identity user SID.')
output runtimeClientId string = runtime.properties.clientId
output storageAccountName string = storageName
output curatedStorageAccountName string = curatedStorageName
output functionsSubnetId string = network.outputs.functionsSubnetId
output aiAccountId string = provisionAiModel ? ai!.outputs.accountId : ''
output aiEndpoint string = provisionAiModel ? ai!.outputs.endpoint : ''
