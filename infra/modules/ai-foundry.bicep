// AI Foundry account and model deployment for the optional AI-assisted workflow composer.
// The parent template creates the managed-identity role assignment after the Function App identity exists.
@description('Name of the AI Foundry (Cognitive Services) account.')
param name string

@description('Azure region for the AI Foundry account, independent of the application and SQL regions.')
param location string

@description('Tags applied to the resources.')
param tags object = {}

@description('Name of the model deployment (also used as the deployment name the API calls via Azure.AI.OpenAI).')
param modelDeploymentName string = 'gpt-5.4-mini'

@description('Underlying model name in the AI Foundry model catalog.')
param modelName string = 'gpt-5.4-mini'

@description('Model version. Verify against the current AI Foundry model catalog in the target region/subscription at deploy time — Azure OpenAI-compatible deployments require an explicit version string.')
param modelVersion string = '2026-03-17'

@description('Deployment SKU capacity in thousands of tokens-per-minute units. Choose a value appropriate for expected workload and regional quota.')
param modelCapacity int = 10

@description('Model deployment SKU. Availability, quota and data residency must be reviewed for the selected model and region.')
param modelSku string = 'GlobalStandard'

// Cognitive Services account names must be globally unique and become the custom subdomain used
// for Entra ID-based (keyless) data-plane authentication — required since disableLocalAuth is set.
resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: name
    // The Function App reaches this account through the private endpoint configured by the parent template.
    publicNetworkAccess: 'Disabled'
    // No API keys — the Function App authenticates with its system-assigned managed identity
    // (Azure.Identity/DefaultAzureCredential), consistent with the SQL/Storage access pattern.
    disableLocalAuth: true
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: modelDeploymentName
  sku: {
    name: modelSku
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
}

@description('Resource ID of the AI Foundry account (used by main.bicep to create its private endpoint and RBAC role assignment).')
output accountId string = account.id

@description('Name of the AI Foundry account.')
output accountName string = account.name

@description('Data-plane inference endpoint of the AI Foundry account (used for the AiFoundryEndpoint app setting).')
output endpoint string = account.properties.endpoint

@description('Name of the model deployment (used for the AiFoundryDeploymentName app setting).')
output deploymentName string = modelDeployment.name
