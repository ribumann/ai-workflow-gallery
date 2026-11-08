// Reusable private endpoint + private DNS zone (with VNet link) for one target resource.
// Used from main.bicep for resources that require private endpoints, such as SQL server, Storage
// account (blob), and AI Foundry.
@description('Name prefix for the private endpoint resource.')
param name string

@description('Azure region for the private endpoint (must match the target resource\'s region).')
param location string

@description('Tags applied to the resources.')
param tags object = {}

@description('Resource ID of the subnet the private endpoint is created in.')
param subnetId string

@description('Resource ID of the target resource (SQL server, storage account, AI Foundry, etc.).')
param privateLinkServiceId string

@description('groupId for the private link service connection, e.g. "sqlServer", "blob", "vault".')
param groupId string

@description('Private DNS zone name for this resource type, e.g. "privatelink.database.windows.net".')
param privateDnsZoneName string

@description('Resource ID of the VNet to link the private DNS zone to.')
param vnetId string

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${name}-connection'
        properties: {
          privateLinkServiceId: privateLinkServiceId
          groupIds: [groupId]
        }
      }
    ]
  }
}

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: privateDnsZoneName
  location: 'global'
  tags: tags
}

resource privateDnsZoneVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: privateDnsZone
  name: '${privateDnsZone.name}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnetId
    }
    registrationEnabled: false
  }
}

resource dnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: privateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'config'
        properties: {
          privateDnsZoneId: privateDnsZone.id
        }
      }
    ]
  }
}
