// Private networking for the AI Workflow Gallery.
// The API reaches SQL, Storage, and AI services over the VNet through private endpoints.
@description('Base name used to derive network resource names.')
param resourceToken string

@description('Azure region for the VNet and its private endpoints/DNS zones.')
param location string

@description('Tags applied to the resources.')
param tags object = {}

@description('Address space for the VNet.')
param addressPrefix string = '10.20.0.0/16'

@description('Address prefix for the subnet the Function App VNet-integrates into.')
param functionsSubnetPrefix string = '10.20.1.0/24'

@description('Address prefix for the subnet holding SQL/Storage/AI Foundry private endpoints.')
param privateEndpointsSubnetPrefix string = '10.20.2.0/24'

@description('Optional authoritative existing NSG association; null leaves the subnet without an NSG. Does not manage the NSG or its rules.')
param functionsSubnetNetworkSecurityGroupId string?

@description('Optional authoritative existing NSG association; null leaves the subnet without an NSG. Does not manage the NSG or its rules.')
param privateEndpointsSubnetNetworkSecurityGroupId string?

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: 'vnet-${resourceToken}'
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [addressPrefix]
    }
    subnets: [
      {
        name: 'snet-functions'
        properties: {
          addressPrefix: functionsSubnetPrefix
          ...(functionsSubnetNetworkSecurityGroupId != null ? {
            networkSecurityGroup: { id: functionsSubnetNetworkSecurityGroupId }
          } : {})
          delegations: [
            {
              // Flex Consumption Function Apps use the Container Apps-style networking model —
              // confirmed via deployment error SubnetMissingRequiredDelegation, which required this
              // exact delegation (not Microsoft.Web/serverFarms, despite some docs suggesting that).
              name: 'delegation-functions'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'snet-privateendpoints'
        properties: {
          addressPrefix: privateEndpointsSubnetPrefix
          ...(privateEndpointsSubnetNetworkSecurityGroupId != null ? {
            networkSecurityGroup: { id: privateEndpointsSubnetNetworkSecurityGroupId }
          } : {})
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

@description('Resource ID of the subnet the Function App VNet-integrates into.')
output functionsSubnetId string = '${vnet.id}/subnets/snet-functions'

@description('Resource ID of the subnet holding private endpoints.')
output privateEndpointsSubnetId string = '${vnet.id}/subnets/snet-privateendpoints'

@description('Resource ID of the VNet.')
output vnetId string = vnet.id
