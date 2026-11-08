// Azure SQL Database with serverless compute and configurable auto-pause.
@description('Name of the logical SQL server.')
param serverName string

@description('Name of the SQL database.')
param databaseName string

@description('Azure region for the SQL server and database.')
param location string

@description('Tags applied to the resources.')
param tags object = {}

@description('Display name of the Microsoft Entra principal configured as SQL server admin. Use a group when multiple identities need access without SQL credentials.')
param aadAdminLogin string

@description('Object ID (sid) of the Microsoft Entra principal to set as the SQL server admin.')
param aadAdminObjectId string

@description('Principal type of the Microsoft Entra SQL admin: "Group" (recommended — lets scripts/deploy-environment.ps1 add the Function App\'s identity as a member post-deploy without redeploying this module) or "User" (a single person, for local/manual setups).')
@allowed([
  'Group'
  'User'
  'Application'
])
param aadAdminPrincipalType string = 'Group'

@description('Microsoft Entra tenant ID that aadAdminObjectId belongs to.')
param aadAdminTenantId string = subscription().tenantId

@description('Minutes of inactivity before the serverless database auto-pauses. Use -1 to disable auto-pause. Azure accepts only -1 or a value of at least 60.')
@allowed([
  -1
  60
  120
  240
  360
  720
  1440
  10080
])
param autoPauseDelayMinutes int = 60

@description('Maximum vCores for the serverless database (Gen5 family).')
param maxVCores int = 2

@description('Minimum vCores for the serverless database.')
param minVCores string = '0.5'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    // Use Microsoft Entra-only authentication. Group membership can grant the Function App's
    // managed identity database access without embedding SQL credentials.
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: aadAdminPrincipalType
      login: aadAdminLogin
      sid: aadAdminObjectId
      tenantId: aadAdminTenantId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    // Database access is expected through the private endpoint configured by the parent template.
    publicNetworkAccess: 'Disabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: maxVCores
  }
  properties: {
    autoPauseDelay: autoPauseDelayMinutes
    minCapacity: json(minVCores)
    zoneRedundant: false
  }
}

@description('Fully-qualified SQL server hostname.')
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName

@description('Resource ID of the SQL server (used by main.bicep to create its private endpoint).')
output serverId string = sqlServer.id

@description('ADO.NET connection string using Microsoft Entra Managed Identity authentication; no SQL password is embedded.')
output connectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${databaseName};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
