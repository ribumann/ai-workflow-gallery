param name string
param location string
param tags object
param subnetId string
param runtimeIdentityId string
param runtimeClientId string
param playgroundStorageIdentityId string
param playgroundStorageClientId string
param curatedReaderIdentityId string
param curatedReaderClientId string
param curatedStorageBlobEndpoint string
param storageAccountName string
param storageBlobEndpoint string
param playgroundStorageBlobEndpoint string
param sqlConnectionString string
@secure()
param applicationInsightsConnectionString string
param galleryOrigin string
param enablePublicComposer bool = false
param enableMcpServer bool = false
param enablePublicMcp bool = false
param enablePublicPlaygroundWrites bool = false
param publicMcpAllowedOrigins array = []
param aiFoundryEndpoint string = ''
param aiFoundryDeploymentName string = ''
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

var composerEnabled = enablePublicComposer && !empty(aiFoundryEndpoint) && !empty(aiFoundryDeploymentName)

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-${name}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: name
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${runtimeIdentityId}': {}
      '${playgroundStorageIdentityId}': {}
      '${curatedReaderIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    virtualNetworkSubnetId: subnetId
    vnetRouteAllEnabled: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storageBlobEndpoint}app-package'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: runtimeIdentityId
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
        alwaysReady: []
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storageAccountName }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: runtimeClientId }
        { name: 'AZURE_CLIENT_ID', value: runtimeClientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: applicationInsightsConnectionString }
        { name: 'SqlConnectionString', value: sqlConnectionString }
        { name: 'StorageBlobEndpoint', value: storageBlobEndpoint }
        { name: 'StorageContainerName', value: 'artifact-files' }
        { name: 'PlaygroundStorageContainerName', value: 'playground-files' }
        { name: 'PlaygroundStorageClientId', value: playgroundStorageClientId }
        { name: 'PlaygroundStorageBlobEndpoint', value: playgroundStorageBlobEndpoint }
        { name: 'CuratedStorageClientId', value: curatedReaderClientId }
        { name: 'CuratedStorageBlobEndpoint', value: curatedStorageBlobEndpoint }
        { name: 'PublicPlaygroundWritesEnabled', value: enablePublicPlaygroundWrites ? 'true' : 'false' }
        { name: 'GalleryBaseUrl', value: galleryOrigin }
        { name: 'PublicDemoEnabled', value: 'true' }
        { name: 'EnableDevSeedData', value: 'false' }
        { name: 'EnableComposerSampleData', value: 'false' }
        { name: 'EnableProdBonusTasks', value: 'false' }
        { name: 'EnableMcpServer', value: enableMcpServer ? 'true' : 'false' }
        { name: 'EnablePublicMcp', value: enablePublicMcp ? 'true' : 'false' }
        { name: 'PublicMcpAllowedOrigins', value: join(publicMcpAllowedOrigins, ',') }
        { name: 'EnableMcpWrites', value: 'false' }
        { name: 'EnableFeedbackEmail', value: 'false' }
        { name: 'EnableAiComposer', value: composerEnabled ? 'true' : 'false' }
        { name: 'PublicComposerEnabled', value: composerEnabled ? 'true' : 'false' }
        { name: 'AiFoundryEndpoint', value: aiFoundryEndpoint }
        { name: 'AiFoundryDeploymentName', value: aiFoundryDeploymentName }
        { name: 'PublicComposer__DailyRequestLimit', value: string(publicComposerDailyRequestLimit) }
        { name: 'PublicComposer__DailyTokenLimit', value: string(publicComposerDailyTokenLimit) }
        { name: 'PublicComposer__MaxConcurrentRequests', value: string(publicComposerMaxConcurrentRequests) }
        { name: 'PublicComposer__MaxGoalCharacters', value: string(publicComposerMaxGoalCharacters) }
        { name: 'PublicComposer__MaxContextBytes', value: string(publicComposerMaxContextBytes) }
        { name: 'PublicComposer__MaxOutputTokens', value: string(publicComposerMaxOutputTokens) }
        { name: 'PublicComposer__ModelTimeoutSeconds', value: string(publicComposerModelTimeoutSeconds) }
      ]
    }
  }
}

resource scmBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'scm'
  properties: { allow: false }
}

resource ftpBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'ftp'
  properties: { allow: false }
}

output id string = app.id
