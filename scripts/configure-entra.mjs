import { execFileSync } from 'node:child_process'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { parseArgs } from 'node:util'

const { values } = parseArgs({
  options: { apply: { type: 'boolean', default: false }, manifest: { type: 'string' }, origin: { type: 'string' },
    'browser-api-only': { type: 'boolean', default: false } },
  strict: true, allowPositionals: false,
})
if (!values.manifest) throw new Error('Provide --manifest with an explicitly reviewed local registration manifest.')
const desired = JSON.parse(await readFile(resolve(values.manifest), 'utf8'))
const guid = /^[0-9a-f-]{36}$/i
if (![desired.subscriptionId, desired.tenantId, desired.scopeId, desired.apiClientId, desired.apiObjectId,
  desired.spaClientId, desired.spaObjectId, desired.referenceClientId, desired.referenceObjectId].every((id) => guid.test(id))) {
  throw new Error('Registration manifest contains an invalid target identifier.')
}

function azure(args) {
  const command = process.platform === 'win32' ? process.env.ComSpec : 'az'
  const parameters = process.platform === 'win32'
    ? ['/d', '/s', '/c', `az ${args.join(' ')}`] : args
  return JSON.parse(execFileSync(command, parameters, {
    encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
  }))
}

const account = azure(['account', 'show', '--subscription', desired.subscriptionId, '--output', 'json'])
if (account.id !== desired.subscriptionId || account.tenantId !== desired.tenantId) {
  throw new Error('Caller Azure subscription/directory does not match the reviewed manifest.')
}
let origin = 'http://localhost:5173'
if (values.origin) {
  const requested = new URL(values.origin)
  if (requested.protocol !== 'https:' || requested.username || requested.password ||
      requested.pathname !== '/' || requested.search || requested.hash) {
    throw new Error('--origin must be an explicit HTTPS gallery origin.')
  }
  origin = requested.origin
}
if (!values.apply) {
  console.log(JSON.stringify({ planOnly: true, ...desired, browserRedirect: origin,
    browserApiOnly: values['browser-api-only'], mcpIdentifierUpdate: !values['browser-api-only'] && !!values.origin,
    browserApiReady: false, mcpReady: false,
    secretsCreated: false, adminConsentGranted: false }, null, 2))
  process.exit(0)
}

// Cached caller credentials stay inside this process. No token is printed or persisted.
const credential = azure(['account', 'get-access-token',
  '--tenant', desired.tenantId, '--resource', 'https://graph.microsoft.com/', '--output', 'json'])
if (credential.tenant !== desired.tenantId) throw new Error('Graph credential directory does not match the reviewed target.')

async function graph(path, method = 'GET', body) {
  const response = await fetch(`https://graph.microsoft.com/v1.0${path}`, {
    method,
    headers: { Authorization: `Bearer ${credential.accessToken}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    redirect: 'error', signal: AbortSignal.timeout(30000),
  })
  if (!response.ok) {
    const error = await response.json()
    throw new Error(`Graph ${method} ${path.split('?')[0]} failed (${response.status}): ${error.error?.code ?? 'unknown_error'}. No consent bypass attempted.`)
  }
  return response.status === 204 ? null : response.json()
}

const me = await graph('/me?$select=id')
async function verifyEnterpriseApplication(name, clientId) {
  const filter = encodeURIComponent(`appId eq '${clientId}'`)
  const matches = await graph(`/servicePrincipals?$filter=${filter}&$select=id,appId,appOwnerOrganizationId,accountEnabled,servicePrincipalType`)
  if (!matches || !Array.isArray(matches.value) || matches['@odata.nextLink'] || matches.value.length !== 1) {
    throw new Error(`Enterprise Application lookup for ${name} is missing, ambiguous or malformed; readiness is not verified. No principal was created or changed.`)
  }
  const principal = matches.value[0]
  if (!principal || typeof principal.id !== 'string' || !guid.test(principal.id) ||
      principal.appId !== clientId || principal.appOwnerOrganizationId !== desired.tenantId ||
      principal.accountEnabled !== true || principal.servicePrincipalType !== 'Application') {
    throw new Error(`Enterprise Application for ${name} does not match the reviewed app ID, home tenant or enabled Application principal; readiness is not verified. No principal was created or changed.`)
  }
}
await verifyEnterpriseApplication(desired.apiName, desired.apiClientId)
await verifyEnterpriseApplication(desired.spaName, desired.spaClientId)
if (!values['browser-api-only'])
  await verifyEnterpriseApplication(desired.referenceClientName, desired.referenceClientId)

async function ownedApplication(name, clientId, objectId, body) {
  const filter = encodeURIComponent(`displayName eq '${name.replaceAll("'", "''")}'`)
  const matches = await graph(`/applications?$filter=${filter}&$select=id,appId,displayName,identifierUris,spa`)
  if (matches['@odata.nextLink'] || matches.value.length > 1) {
    throw new Error(`Ambiguous application name ${name}; refusing replacement.`)
  }
  if (matches.value.length === 1) {
    const app = matches.value[0]
    if (app.id !== objectId || app.appId !== clientId)
      throw new Error(`Existing ${name} does not match the reviewed owned application IDs.`)
    const owners = await graph(`/applications/${app.id}/owners?$select=id`)
    if (!owners.value.some((owner) => owner.id === me.id)) {
      throw new Error(`Existing ${name} is not owned by the verified caller.`)
    }
    if (values['browser-api-only'] && name === desired.apiName &&
        !app.identifierUris?.includes(`api://${app.appId}`))
      throw new Error('Browser/API-only mode requires the existing api://GUID identifier; it will not rewrite identifierUris.')
    const update = body.spa
      ? { ...body, spa: { ...body.spa, redirectUris: [...new Set([
        ...(app.spa?.redirectUris ?? []), ...body.spa.redirectUris,
      ])] } }
      : body
    await graph(`/applications/${app.id}`, 'PATCH', update)
    return app
  }
  throw new Error(`Reviewed ${name} is missing; refusing to create a replacement.`)
}

const api = await ownedApplication(desired.apiName, desired.apiClientId, desired.apiObjectId, {
  signInAudience: 'AzureADMultipleOrgs',
  api: {
    requestedAccessTokenVersion: 2,
    oauth2PermissionScopes: [{
      id: desired.scopeId, value: desired.scope, type: 'User', isEnabled: true,
      adminConsentDisplayName: 'Use the shared gallery as the signed-in person',
      adminConsentDescription: 'Read and contribute to the shared gallery subject to directory admission and existing feature permissions.',
      userConsentDisplayName: 'Use the gallery as you',
      userConsentDescription: 'Read and contribute to the shared gallery with your identity and existing permissions.',
    }],
  },
  requiredResourceAccess: [],
})
if (!values['browser-api-only']) {
  await graph(`/applications/${api.id}`, 'PATCH', {
    identifierUris: [...new Set([
      ...(api.identifierUris ?? []), `api://${api.appId}`, ...(values.origin ? [`${origin}/api/mcp`] : []),
    ])],
  })
}
const permission = [{ resourceAppId: api.appId, resourceAccess: [{ id: desired.scopeId, type: 'Scope' }] }]
const spa = await ownedApplication(desired.spaName, desired.spaClientId, desired.spaObjectId, {
  signInAudience: 'AzureADMultipleOrgs',
  spa: { redirectUris: [...new Set(['http://localhost:5173', origin])] },
  requiredResourceAccess: permission,
})
const reference = values['browser-api-only'] ? null : await ownedApplication(desired.referenceClientName, desired.referenceClientId, desired.referenceObjectId, {
  signInAudience: 'AzureADMultipleOrgs', isFallbackPublicClient: true,
  publicClient: { redirectUris: ['http://localhost'] },
  requiredResourceAccess: permission,
})

const verifiedApi = await graph(`/applications/${api.id}?$select=id,appId,signInAudience,identifierUris,api`)
const verifiedSpa = await graph(`/applications/${spa.id}?$select=id,appId,signInAudience,spa,requiredResourceAccess`)
const verifiedScope = verifiedApi.api?.oauth2PermissionScopes?.find(s => s.id === desired.scopeId)
if (verifiedApi.id !== desired.apiObjectId || verifiedApi.appId !== desired.apiClientId ||
    verifiedApi.signInAudience !== 'AzureADMultipleOrgs' || verifiedApi.api?.requestedAccessTokenVersion !== 2 ||
    !verifiedApi.identifierUris?.includes(`api://${desired.apiClientId}`) ||
    !api.identifierUris?.every(uri => verifiedApi.identifierUris.includes(uri)) ||
    verifiedScope?.value !== desired.scope || verifiedScope?.type !== 'User' || verifiedScope?.isEnabled !== true ||
    verifiedSpa.id !== desired.spaObjectId || verifiedSpa.appId !== desired.spaClientId ||
    verifiedSpa.signInAudience !== 'AzureADMultipleOrgs' || !verifiedSpa.spa?.redirectUris?.includes(origin) ||
    !spa.spa?.redirectUris?.every(uri => verifiedSpa.spa.redirectUris.includes(uri)) ||
    !verifiedSpa.requiredResourceAccess?.some(resource => resource.resourceAppId === desired.apiClientId &&
      resource.resourceAccess?.some(access => access.id === desired.scopeId && access.type === 'Scope'))) {
  throw new Error('Owned browser/API registration read-back does not match the reviewed scope, audience, IDs or preserved identifiers/redirects.')
}
if (!values['browser-api-only'] && values.origin && !verifiedApi.identifierUris.includes(`${origin}/api/mcp`))
  throw new Error('Full MCP registration read-back is missing the exact canonical HTTPS identifier.')

console.log(JSON.stringify({
  tenantId: desired.tenantId, subscriptionId: desired.subscriptionId,
  api: { clientId: api.appId, objectId: api.id },
  browser: { clientId: spa.appId, objectId: spa.id, redirectUri: origin },
  reference: reference ? { clientId: reference.appId, objectId: reference.id } : null,
  browserApiOnly: values['browser-api-only'],
  browserApiReady: true,
  mcpReady: !values['browser-api-only'] && !!values.origin,
  scope: `api://${api.appId}/${desired.scope}`,
  adminConsentGranted: false, secretsCreated: false,
}, null, 2))
