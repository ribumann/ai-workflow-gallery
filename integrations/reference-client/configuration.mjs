import { parseArgs } from 'node:util'

export function readConfiguration(args = process.argv.slice(2)) {
  const { values, tokens } = parseArgs({
    args,
    options: {
      endpoint: { type: 'string' },
      anonymous: { type: 'boolean', default: false },
      'collection-id': { type: 'string' },
      tenant: { type: 'string' },
      'client-id': { type: 'string' },
      'api-client-id': { type: 'string' },
    },
    strict: true,
    allowPositionals: false,
    tokens: true,
  })
  const seen = new Set()
  for (const token of tokens) {
    if (token.kind !== 'option') continue
    if (seen.has(token.name)) throw new Error(`--${token.name} must not be repeated.`)
    seen.add(token.name)
  }
  if (!['public-techniques', 'curated-tools', 'playground'].includes(values['collection-id'])) {
    throw new Error('--collection-id must be explicitly public-techniques, curated-tools or playground.')
  }
  const endpoint = new URL(values.endpoint)
  if (endpoint.protocol !== 'https:' || endpoint.pathname !== '/api/mcp' ||
      endpoint.username || endpoint.password || endpoint.search || endpoint.hash) {
    throw new Error('--endpoint must be the exact HTTPS gallery /api/mcp resource.')
  }
  const common = { endpoint, collectionId: values['collection-id'], anonymous: values.anonymous }
  if (values.anonymous) {
    if (['tenant', 'client-id', 'api-client-id'].some((key) => values[key] !== undefined)) {
      throw new Error('--anonymous cannot be combined with Entra authentication options.')
    }
    return common
  }
  const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
  for (const key of ['tenant', 'client-id', 'api-client-id']) {
    if (!values[key] || !guid.test(values[key]) || values[key] === '00000000-0000-0000-0000-000000000000') {
      throw new Error(`--${key} must be an explicit nonempty Entra GUID.`)
    }
  }
  return {
    ...common,
    metadata: new URL('/api/oauth-resource-metadata', endpoint),
    authority: `https://login.microsoftonline.com/${values.tenant}/v2.0`,
    tenant: values.tenant,
    clientId: values['client-id'],
    scope: `api://${values['api-client-id']}/Gallery.Access`,
  }
}

export function verifyMetadata(metadata, configuration) {
  if (metadata.resource !== configuration.endpoint.href ||
      !metadata.authorization_servers?.includes(configuration.authority) ||
      !metadata.scopes_supported?.includes(configuration.scope) ||
      !metadata.bearer_methods_supported?.includes('header')) {
    throw new Error('Protected-resource metadata does not match the explicitly chosen resource, authority and delegated permission.')
  }
}
