// Blob Storage account with private containers for artifact uploads and the Function package.
// File access is brokered by the API; anonymous blob access is disabled.
@description('Base name used to derive the globally-unique storage account name.')
param baseName string

@description('Azure region for the storage account.')
param location string

@description('Tags applied to the resource.')
param tags object = {}

@description('Name of the private container used for artifact file uploads.')
param containerName string = 'artifact-files'

@description('Name of the container used to hold the Function App\'s Flex Consumption deployment package.')
param deploymentContainerName string = 'app-package'

// Storage account names must be 3-24 lowercase alphanumeric characters and globally unique.
var storageAccountName = toLower(take('st${replace(baseName, '-', '')}${uniqueString(resourceGroup().id)}', 24))

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    // The Function App reaches this account through the private endpoint configured by the parent template.
    publicNetworkAccess: 'Disabled'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource artifactContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: deploymentContainerName
  properties: {
    publicAccess: 'None'
  }
}

@description('Name of the storage account.')
output storageAccountName string = storageAccount.name

@description('Resource ID of the storage account (used by main.bicep to create its private endpoint and RBAC role assignment).')
output storageAccountId string = storageAccount.id

@description('Name of the private artifact-files container.')
output containerName string = artifactContainer.name

@description('Name of the Function App deployment-package container.')
output deploymentContainerName string = deploymentContainer.name

@description('Blob endpoint of the storage account (used for identity-based Function App storage app settings).')
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
