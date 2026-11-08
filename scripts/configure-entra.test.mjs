import { test, mock } from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'

const manifestPath = fileURLToPath(new URL('./fixtures/entra-registration.test.json', import.meta.url))
const desired = JSON.parse(await readFile(manifestPath, 'utf8'))
const origin = 'https://gallery.example.org'
let revision = 0

async function runRegistration(t, { browserOnly = true, apply = true, missing = false, wrongId = false, owner = true,
  rejectIdentifier = false, staleReadBack = false, principalResponse, principalStatus = 200,
  principalClientId = desired.apiClientId } = {}) {
  const calls = []
  const output = []
  const azureCalls = []
  const apps = [
    { id: desired.apiObjectId, appId: desired.apiClientId, displayName: desired.apiName,
      identifierUris: [`api://${desired.apiClientId}`, 'api://preserved-existing-identifier'] },
    { id: desired.spaObjectId, appId: desired.spaClientId, displayName: desired.spaName,
      spa: { redirectUris: ['https://preserved.example'] } },
    { id: desired.referenceObjectId, appId: desired.referenceClientId, displayName: desired.referenceClientName },
  ]
  t.mock.module('node:child_process', { namedExports: {
    execFileSync: (_command, args) => {
      azureCalls.push(args.join(' '))
      return JSON.stringify(args.join(' ').includes('get-access-token')
        ? { tenant: desired.tenantId, accessToken: 'test-only-credential' }
        : { id: desired.subscriptionId, tenantId: desired.tenantId })
    },
  } })
  t.mock.method(globalThis, 'fetch', async (url, options) => {
    const path = decodeURIComponent(new URL(url).pathname + new URL(url).search).replace('/v1.0', '')
    const body = options.body ? JSON.parse(options.body) : undefined
    calls.push({ path, method: options.method, body })
    if (path.startsWith('/me?')) return Response.json({ id: 'test-owner' })
    if (path.startsWith('/servicePrincipals?')) {
      assert.equal(options.method, 'GET')
      const app = apps.find(a => path.includes(`appId eq '${a.appId}'`))
      assert.ok(app, `Unexpected principal lookup ${path}`)
      return Response.json(app.appId === principalClientId && principalResponse !== undefined
        ? principalResponse
        : { value: [{ id: '11111111-2222-4333-8444-555555555555', appId: app.appId,
          appOwnerOrganizationId: desired.tenantId, accountEnabled: true, servicePrincipalType: 'Application' }] },
      { status: app.appId === principalClientId ? principalStatus : 200 })
    }
    if (path.includes('/owners?')) return Response.json({ value: owner ? [{ id: 'test-owner' }] : [] })
    if (options.method === 'PATCH') {
      if (rejectIdentifier && body.identifierUris)
        return Response.json({ error: { code: 'HostNameNotOnVerifiedDomain' } }, { status: 400 })
      const app = apps.find(a => path === `/applications/${a.id}`)
      assert.ok(app)
      if (!staleReadBack) Object.assign(app, structuredClone(body))
      return new Response(null, { status: 204 })
    }
    const exactApp = apps.find(a => path.startsWith(`/applications/${a.id}?`))
    if (exactApp) return Response.json(exactApp)
    const app = apps.find(a => path.includes(`displayName eq '${a.displayName}'`))
    assert.ok(app, `Unexpected Graph request ${path}`)
    return Response.json({ value: missing ? [] : [{ ...structuredClone(app), ...(wrongId ? { appId: 'unexpected-id' } : {}) }] })
  })
  t.mock.method(console, 'log', text => output.push(JSON.parse(text)))
  t.mock.method(process, 'exit', code => {
    assert.equal(code, 0)
    throw new Error('test-plan-exit')
  })
  const previous = process.argv
  process.argv = ['node', 'configure-entra.mjs', '--manifest', manifestPath,
    ...(apply ? ['--apply'] : []), '--origin', origin,
    ...(browserOnly ? ['--browser-api-only'] : [])]
  try {
    await import(`./configure-entra.mjs?test=${revision++}`)
  } catch (error) {
    assert.ok(!output.some(result => result.browserApiReady || result.mcpReady))
    if (apply || error.message !== 'test-plan-exit') {
      assert.ok(calls.filter(c => c.path.startsWith('/servicePrincipals')).every(c => c.method === 'GET'))
      if (principalResponse !== undefined || principalStatus !== 200)
        assert.ok(!calls.some(c => c.method !== 'GET'))
      throw error
    }
  } finally {
    process.argv = previous
    mock.restoreAll()
  }
  return { calls, output, azureCalls }
}

test('browser/API-only preserves API identifiers and SPA redirects; skips reference registration', async t => {
  const { calls, output } = await runRegistration(t)
  const writes = calls.filter(c => c.method !== 'GET')
  assert.equal(writes.length, 2)
  assert.ok(writes.every(c => c.method === 'PATCH'))
  assert.ok(writes.every(c => !Object.hasOwn(c.body, 'identifierUris')))
  assert.equal(writes[0].body.signInAudience, 'AzureADMultipleOrgs')
  assert.equal(writes[0].body.api.requestedAccessTokenVersion, 2)
  assert.equal(writes[0].body.api.oauth2PermissionScopes[0].value, 'Gallery.Access')
  assert.deepEqual(writes[1].body.spa.redirectUris, ['https://preserved.example', 'http://localhost:5173', origin])
  assert.ok(!calls.some(c => c.path.includes(desired.referenceClientName)))
  assert.ok(!calls.some(c => c.path.includes(desired.referenceClientId)))
  assert.equal(calls.filter(c => c.path.startsWith('/servicePrincipals?')).length, 2)
  assert.equal(output[0].browserApiReady, true)
  assert.equal(output[0].mcpReady, false)
  assert.equal(output[0].secretsCreated, false)
  assert.equal(output[0].adminConsentGranted, false)
})

test('full MCP mode retains identifiers and adds the exact canonical HTTPS resource', async t => {
  const { calls, output } = await runRegistration(t, { browserOnly: false })
  const update = calls.find(c => c.body?.identifierUris)
  assert.deepEqual(update.body.identifierUris, [
    `api://${desired.apiClientId}`, 'api://preserved-existing-identifier', `${origin}/api/mcp`,
  ])
  assert.equal(calls.filter(c => c.path.startsWith('/servicePrincipals?')).length, 3)
  assert.equal(output[0].mcpReady, true)
})

test('plan mode does not query Graph, obtain a token or claim readiness', async t => {
  const { calls, output, azureCalls } = await runRegistration(t, { apply: false })
  assert.deepEqual(calls, [])
  assert.equal(azureCalls.length, 1)
  assert.ok(!azureCalls[0].includes('get-access-token'))
  assert.equal(output[0].planOnly, true)
  assert.equal(output[0].browserApiReady, false)
  assert.equal(output[0].mcpReady, false)
})

const validPrincipal = {
  id: '11111111-2222-4333-8444-555555555555', appId: desired.apiClientId,
  appOwnerOrganizationId: desired.tenantId, accountEnabled: true, servicePrincipalType: 'Application',
}
for (const [name, principalResponse] of [
  ['missing principal', { value: [] }],
  ['ambiguous principals', { value: [validPrincipal, validPrincipal] }],
  ['paged lookup', { value: [validPrincipal], '@odata.nextLink': 'https://graph.microsoft.com/next' }],
  ['malformed lookup', {}],
  ['malformed value', { value: {} }],
  ['null lookup', null],
  ['null principal', { value: [null] }],
  ['invalid object ID', { value: [{ ...validPrincipal, id: 'invalid' }] }],
  ['wrong app ID', { value: [{ ...validPrincipal, appId: desired.spaClientId }] }],
  ['wrong home tenant', { value: [{ ...validPrincipal, appOwnerOrganizationId: 'other-tenant' }] }],
  ['disabled principal', { value: [{ ...validPrincipal, accountEnabled: false }] }],
  ['missing enabled state', { value: [{ ...validPrincipal, accountEnabled: undefined }] }],
  ['wrong principal type', { value: [{ ...validPrincipal, servicePrincipalType: 'ManagedIdentity' }] }],
]) {
  test(`registration rejects ${name} before writes or readiness`, async t => {
    await assert.rejects(runRegistration(t, { principalResponse }), /Enterprise Application/)
  })
}

test('registration fails closed on denied Enterprise Application read', async t => {
  await assert.rejects(runRegistration(t, {
    principalStatus: 403, principalResponse: { error: { code: 'Authorization_RequestDenied' } },
  }), /Authorization_RequestDenied/)
})

for (const [name, browserOnly, principalClientId] of [
  ['SPA', true, desired.spaClientId],
  ['full-MCP reference', false, desired.referenceClientId],
]) {
  test(`registration requires the ${name} Enterprise Application too`, async t => {
    await assert.rejects(runRegistration(t, {
      browserOnly, principalClientId, principalResponse: { value: [] },
    }), /Enterprise Application/)
  })
}

test('full MCP mode fails closed on the observed Graph hostname denial', async t => {
  await assert.rejects(runRegistration(t, { browserOnly: false, rejectIdentifier: true }), /HostNameNotOnVerifiedDomain/)
})

test('registration cannot report readiness without authoritative read-back', async t => {
  await assert.rejects(runRegistration(t, { staleReadBack: true }), /read-back does not match/)
})

for (const [name, options, error] of [
  ['missing owned app', { missing: true }, /refusing to create a replacement/],
  ['unexpected app ID', { wrongId: true }, /reviewed owned application IDs/],
  ['not owned by caller', { owner: false }, /not owned by the verified caller/],
]) {
  test(`registration rejects ${name}`, async t => {
    await assert.rejects(runRegistration(t, options), error)
  })
}
