// Static Web App hosting the frontend and linking the API Function App as its backend.
@description('Name of the Static Web App resource.')
param name string

@description('Azure region for the Static Web App (SWA has a restricted set of supported regions).')
param location string

@description('Tags applied to the resource.')
param tags object = {}

@description('Resource ID of the Function App to link as this Static Web App\'s backend.')
param linkedFunctionAppId string

@description('Azure region of the linked Function App.')
param linkedFunctionAppLocation string

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    // Source control is left unconfigured here; deployment is performed by the disabled-by-default
    // GitHub Actions workflow (deploy.yml) using the SWA deployment token, per Constitution Principle VI.
    provider: 'None'
    buildProperties: {
      appLocation: 'frontend'
      outputLocation: 'dist'
    }
  }
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
}

// Links the standalone Function App as this SWA's backend so `/api/*` requests from the frontend
// are proxied to it transparently.
resource linkedBackend 'Microsoft.Web/staticSites/linkedBackends@2023-12-01' = {
  parent: staticWebApp
  name: 'api'
  properties: {
    backendResourceId: linkedFunctionAppId
    region: linkedFunctionAppLocation
  }
}

@description('Default hostname of the deployed Static Web App.')
output defaultHostname string = staticWebApp.properties.defaultHostname

@description('Resource ID of the Static Web App.')
output id string = staticWebApp.id

@description('Name of the Static Web App resource.')
output name string = staticWebApp.name
