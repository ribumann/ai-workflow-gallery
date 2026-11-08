// Application Insights (workspace-based) + its backing Log Analytics workspace, per
// architecture.md's monitoring component. Feeds the Functions API's OpenTelemetry pipeline
// (host.json telemetryMode: "OpenTelemetry") via APPLICATIONINSIGHTS_CONNECTION_STRING.
@description('Base name used to derive the Log Analytics workspace and Application Insights names.')
param resourceToken string

@description('Azure region for the workspace and Application Insights resource.')
param location string

@description('Tags applied to the resources.')
param tags object = {}

@description('Days to retain Log Analytics data (kept short for an MVP to limit cost).')
param retentionInDays int = 30

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    IngestionMode: 'LogAnalytics'
  }
}

@description('Connection string consumed by the Functions API app setting (APPLICATIONINSIGHTS_CONNECTION_STRING).')
output connectionString string = appInsights.properties.ConnectionString

@description('Name of the Application Insights resource.')
output name string = appInsights.name
