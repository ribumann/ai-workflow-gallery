using '../main.bicep'

param appName = 'wfgallery'
param environmentName = 'dev'
param location = 'eastus2'
param enableDevSeedData = true

// Provide the Entra SQL administrator identity through environment variables. The deployment
// script configures the runtime managed identity's database access for the selected environment.
param aadAdminLogin = readEnvironmentVariable('AAD_ADMIN_LOGIN', '')
param aadAdminObjectId = readEnvironmentVariable('AAD_ADMIN_OBJECT_ID', '')

// Set this to the directory whose users should be allowed to sign in. It may differ from the
// directory that owns the Azure subscription.
param allowedTenantId = readEnvironmentVariable('ALLOWED_TENANT_ID')

// Required for compatibility with the Function App's legacy configuration setting.
param allowedEmailDomain = readEnvironmentVariable('ALLOWED_EMAIL_DOMAIN')

// Feedback email uses Azure Communication Services Email plus the Function App managed identity.
// Sending defaults disabled. Explicitly opt in only after configuring endpoint/sender/recipient
// and separately approving runtime authorization; there is no recipient fallback.
param feedbackEmailEndpoint = readEnvironmentVariable('FEEDBACK_EMAIL_ENDPOINT', '')
param feedbackEmailSenderAddress = readEnvironmentVariable('FEEDBACK_EMAIL_SENDER_ADDRESS', '')
param feedbackEmailRecipientAddress = readEnvironmentVariable('FEEDBACK_EMAIL_RECIPIENT_ADDRESS', '')
