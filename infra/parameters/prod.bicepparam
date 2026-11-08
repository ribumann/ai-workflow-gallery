using '../main.bicep'

// Production profile: no seed or mock data; content is supplied by users.
param appName = 'wfgallery'
param environmentName = 'prod'
param location = 'westeurope'
// Override the SQL region independently when required by service availability or architecture.
param sqlLocation = 'swedencentral'
param enableDevSeedData = false

// Enable the AI-assisted workflow composer when it has been validated for this deployment.
param enableAiComposer = true
param aiFoundryLocation = 'swedencentral'
param enableComposerSampleData = false

// Add generic sample tasks to provide a starting catalog. Seeding is idempotent and additive.
param enableProdBonusTasks = true

// Enable the MCP server and its write tools only after validating their access policy and
// operational behavior for this deployment.
param enableMcpServer = true
param enableMcpWrites = true
// Set explicitly rather than inferred from forwarding headers: the "open in the gallery" link on
// every MCP result is what makes an agent's answer citable, so it should not depend on a header a
// caller can influence.
param galleryBaseUrl = 'https://gallery-prod.example.com'

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

// Keep one instance warm and disable SQL auto-pause for consistent production response times.
param alwaysReadyHttpInstances = 1
param sqlAutoPauseDelayMinutes = -1
