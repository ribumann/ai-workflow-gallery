// Flex Consumption Function App hosting the API with VNet integration for private service access.
@description('Name of the Function App resource.')
param name string

@description('Azure region for the Function App.')
param location string

@description('Tags applied to the resources.')
param tags object = {}

@description('Resource ID of the subnet the Function App VNet-integrates into.')
param subnetId string

@description('Name of the storage account backing the Function App runtime + deployment package.')
param storageAccountName string

@description('Blob endpoint of the storage account (for the identity-based deployment.storage app setting).')
param storageBlobEndpoint string

@description('Name of the blob container holding the Flex Consumption deployment package.')
param deploymentContainerName string

@description('Application Insights connection string.')
@secure()
param applicationInsightsConnectionString string

@description('Number of always-ready Flex Consumption instances for the HTTP trigger group. 1 keeps a single instance warm and removes the ~2.4s cold start that dominates perceived latency on this low-traffic app. Defaults to 0 — pure scale-to-zero — so that keeping an instance permanently billed is always an explicit choice made by the calling environment.')
@minValue(0)
@maxValue(10)
param alwaysReadyHttpInstances int = 0

@description('SQL connection string, using Microsoft Entra Managed Identity authentication (no secret embedded).')
param sqlConnectionString string

@description('Name of the blob container used for task file uploads.')
param storageContainerName string

@description('Microsoft Entra tenant ID passed to the Function App for tenant-aware role lookup.')
param allowedTenantId string

@description('Client ID of the registered Entra API.')
param entraApiClientId string

@description('Delegated scope required by the API.')
param entraRequiredScope string

@description('Allows the Microsoft tenant only when it is also in allowedTenantId.')
param allowMicrosoftTenant bool = false

@description('HTTPS MCP resource URL announced in OAuth metadata.')
param entraMcpResource string

@description('Legacy email-domain setting passed to the Function App; it is not the API authorization boundary.')
param allowedEmailDomain string

@description('Azure Communication Services endpoint used by the Feedback Function to send maintainer emails. Empty means feedback returns 502 until configured.')
param feedbackEmailEndpoint string = ''

@description('Verified ACS Email sender address. Empty means feedback returns 502 until configured.')
param feedbackEmailSenderAddress string = ''

@description('Mailbox that receives feedback submissions.')
param feedbackEmailRecipientAddress string = ''

@description('Enables runtime feedback email only after explicit configuration and separately approved send authorization.')
param enableFeedbackEmail bool = false

@description('Seeds a handful of sample tasks and a linking workflow on Functions cold start (SeedData.cs, idempotent). Intended for the UAT environment only — leave false for prod (T060).')
param enableDevSeedData bool = false

@description('Data-plane inference endpoint of the AI Foundry account (spec 003, AI-assisted workflow composer). Empty string is valid — the composer feature is fully gated by enableAiComposer.')
param aiFoundryEndpoint string = ''

@description('Name of the AI Foundry model deployment the composer calls (spec 003).')
param aiFoundryDeploymentName string = ''

@description('Enables the AI-assisted workflow composer feature (spec 003). Set true for uat.bicepparam; kept false for prod.bicepparam until validated in UAT.')
param enableAiComposer bool = false

@description('Seeds additional sample tasks/workflows for AI composer testing on Functions cold start (UAT only, spec 003 Sample Data phase).')
param enableComposerSampleData bool = false

@description('Adds five generic, business-productivity sample tasks to the catalog on Functions cold start (idempotent, additive-only — never touches existing real content).')
param enableProdBonusTasks bool = false

@description('Enables the Model Context Protocol server at /api/mcp (spec 007). Off by default so the endpoint is inert until an environment deliberately opts in.')
param enableMcpServer bool = false

@description('Enables MCP write capabilities (spec 007, FR-007) — the twelve create/update/delete tools. When false they are absent from tools/list and rejected on call, so write access can be switched off independently of read access.')
param enableMcpWrites bool = false

@description('Public origin of the gallery, used to build the "open in the gallery" link on every MCP result. Empty means the origin is inferred from the Static Web Apps forwarding headers.')
param galleryBaseUrl string = ''

var planName = 'plan-${name}'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
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

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: name
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    virtualNetworkSubnetId: subnetId
    vnetRouteAllEnabled: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storageBlobEndpoint}${deploymentContainerName}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
        // Keep instances warm. Without this, Flex Consumption scales to zero between bursts and
        // every sporadic request pays ~2.4s of host initialisation (measured: `InProc init` p50
        // 2385ms, 9 cold starts in 35 minutes of interactive use), which dwarfs the ~16ms p50 the
        // request handler itself costs. See docs/performance-baseline.md.
        alwaysReady: [
          {
            name: 'http'
            instanceCount: alwaysReadyHttpInstances
          }
        ]
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsightsConnectionString
        }
        {
          name: 'SqlConnectionString'
          value: sqlConnectionString
        }
        {
          name: 'StorageBlobEndpoint'
          value: storageBlobEndpoint
        }
        {
          name: 'StorageContainerName'
          value: storageContainerName
        }
        {
          name: 'AllowedTenantId'
          value: allowedTenantId
        }
        {
          name: 'Entra__ApiClientId'
          value: entraApiClientId
        }
        {
          name: 'Entra__RequiredScope'
          value: entraRequiredScope
        }
        {
          name: 'Entra__AllowedTenantIds'
          value: allowedTenantId
        }
        {
          name: 'Entra__AllowMicrosoftTenant'
          value: string(allowMicrosoftTenant)
        }
        {
          name: 'Entra__McpResource'
          value: entraMcpResource
        }
        {
          name: 'AllowedEmailDomain'
          value: allowedEmailDomain
        }
        {
          name: 'FeedbackEmailEndpoint'
          value: feedbackEmailEndpoint
        }
        {
          name: 'EnableFeedbackEmail'
          value: enableFeedbackEmail ? 'true' : 'false'
        }
        {
          name: 'FeedbackEmailSenderAddress'
          value: feedbackEmailSenderAddress
        }
        {
          name: 'FeedbackEmailRecipientAddress'
          value: feedbackEmailRecipientAddress
        }
        {
          name: 'EnableDevSeedData'
          value: string(enableDevSeedData)
        }
        {
          name: 'AiFoundryEndpoint'
          value: aiFoundryEndpoint
        }
        {
          name: 'AiFoundryDeploymentName'
          value: aiFoundryDeploymentName
        }
        {
          name: 'EnableAiComposer'
          value: string(enableAiComposer)
        }
        {
          name: 'EnableComposerSampleData'
          value: string(enableComposerSampleData)
        }
        {
          name: 'EnableProdBonusTasks'
          value: string(enableProdBonusTasks)
        }
        {
          name: 'EnableMcpServer'
          value: string(enableMcpServer)
        }
        {
          name: 'EnableMcpWrites'
          value: string(enableMcpWrites)
        }
        {
          name: 'GalleryBaseUrl'
          value: galleryBaseUrl
        }
      ]
      cors: {
        allowedOrigins: [
          'https://portal.azure.com'
        ]
      }
    }
  }
}

@description('Resource ID of the Function App (used by static-web-app.bicep\'s linkedBackends).')
output id string = functionApp.id

@description('Name of the Function App resource.')
output name string = functionApp.name

@description('Default hostname of the Function App.')
output defaultHostname string = functionApp.properties.defaultHostName

@description('Principal ID of the Function App system-assigned identity — used by main.bicep to grant Storage RBAC and ACS Email sender access.')
output principalId string = functionApp.identity.principalId
