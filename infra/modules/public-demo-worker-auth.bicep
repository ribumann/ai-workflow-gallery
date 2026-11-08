// Authorization is enforced by the worker's request-scoped public/private policy.
// SWA headers/cookies and this setting do not independently authorize data operations.
param functionAppName string

resource app 'Microsoft.Web/sites@2024-04-01' existing = {
  name: functionAppName
}

resource auth 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: app
  name: 'authsettingsV2'
  properties: {
    platform: {
      enabled: false
    }
    globalValidation: {
      requireAuthentication: false
      unauthenticatedClientAction: 'AllowAnonymous'
    }
    httpSettings: {
      requireHttps: true
    }
  }
}
