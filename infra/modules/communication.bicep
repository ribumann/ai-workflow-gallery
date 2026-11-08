// Azure Communication Services + Email Communication Service used by the Feedback Function to mail
// maintainer notifications (spec 002 → feedback via email). Mirrors ai-foundry.bicep's pattern: this
// module provisions the resources only — the principal-consuming role assignment that grants the
// Function App's managed identity send access happens in main.bicep, once its principalId is known.
//
// Both resource types are `global` by ARM contract; `dataLocation` is what pins where message data
// is stored, so it is the knob that matters for compliance, not `location`.
@description('Name of the Communication Services resource.')
param name string

@description('Name of the Email Communication Service that owns the sender domain.')
param emailServiceName string

@description('Tags applied to the resources.')
param tags object = {}

@description('Geography in which Communication Services stores data. Must be a supported ACS data location (e.g. Europe, United States).')
param dataLocation string = 'Europe'

@description('Sender mailbox local-part on the Azure managed domain. ACS provisions "DoNotReply" on every managed domain, so the default needs no extra configuration.')
param senderUsername string = 'DoNotReply'

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: emailServiceName
  location: 'global'
  tags: tags
  properties: {
    dataLocation: dataLocation
  }
}

// An Azure managed domain needs no DNS records and is verified on creation, which keeps the whole
// feedback path deployable without owning a mail domain. It is rate limited and lands in spam more
// readily than a custom domain — acceptable for low-volume maintainer notifications, and the reason
// the sender address is surfaced as an output rather than hardcoded anywhere else.
resource managedDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: name
  location: 'global'
  tags: tags
  properties: {
    dataLocation: dataLocation
    linkedDomains: [
      managedDomain.id
    ]
  }
}

@description('Resource ID of the Communication Services resource (used by main.bicep for the RBAC role assignment).')
output communicationServiceId string = communicationService.id

@description('Name of the Communication Services resource.')
output communicationServiceName string = communicationService.name

@description('Data-plane endpoint used for the FeedbackEmailEndpoint app setting.')
output endpoint string = 'https://${communicationService.properties.hostName}/'

@description('Verified sender address on the Azure managed domain, used for the FeedbackEmailSenderAddress app setting.')
output senderAddress string = '${senderUsername}@${managedDomain.properties.mailFromSenderDomain}'
