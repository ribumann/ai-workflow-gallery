import test from 'node:test'
import assert from 'node:assert/strict'
import { readConfiguration, verifyMetadata } from './configuration.mjs'

const args = [
  '--endpoint', 'https://gallery.example/api/mcp',
  '--collection-id', 'public-techniques',
  '--tenant', '11111111-1111-4111-8111-111111111111',
  '--client-id', '22222222-2222-4222-8222-222222222222',
  '--api-client-id', '33333333-3333-4333-8333-333333333333',
]

test('binds resource, directory-specific issuer and delegated scope', () => {
  const config = readConfiguration(args)
  verifyMetadata({
    resource: config.endpoint.href, authorization_servers: [config.authority],
    scopes_supported: [config.scope], bearer_methods_supported: ['header'],
  }, config)
  assert.equal(config.scope, 'api://33333333-3333-4333-8333-333333333333/Gallery.Access')
})

test('rejects credentials, insecure transport and another resource', () => {
  for (const url of ['http://gallery.example/api/mcp', 'https://user:secret@gallery.example/api/mcp',
    'https://gallery.example/api/mcp?token=secret', 'https://gallery.example/another']) {
    assert.throws(() => readConfiguration([args[0], url, ...args.slice(2)]))
  }
})

test('uses same-origin backend discovery for SWA and direct Functions endpoints', () => {
  for (const origin of ['https://gallery.example', 'https://gallery.azurewebsites.net']) {
    const config = readConfiguration([args[0], `${origin}/api/mcp`, ...args.slice(2)])
    assert.equal(config.metadata.href, `${origin}/api/oauth-resource-metadata`)
    assert.equal(config.metadata.origin, config.endpoint.origin)
  }
})

test('rejects unexpected discovery, without switching authority or resource', () => {
  const config = readConfiguration(args)
  assert.throws(() => verifyMetadata({ resource: 'https://other.example/api/mcp' }, config))
})

test('anonymous mode requires no Entra identifiers or discovery configuration', () => {
  const config = readConfiguration(['--endpoint', args[1], '--anonymous', '--collection-id', 'curated-tools'])
  assert.equal(config.anonymous, true)
  assert.equal(config.collectionId, 'curated-tools')
  for (const key of ['metadata', 'authority', 'tenant', 'clientId', 'scope']) assert.equal(config[key], undefined)
  for (const key of ['tenant', 'client-id', 'api-client-id']) {
    assert.throws(() => readConfiguration([
      '--endpoint', args[1], '--anonymous', '--collection-id', 'public-techniques', `--${key}`, args[5],
    ]), /cannot be combined/)
  }
})

test('both auth modes require an explicit supported collection', () => {
  for (const mode of [[], ['--anonymous']]) {
    for (const collection of [[], ['--collection-id', ''], ['--collection-id', 'all']]) {
      assert.throws(() => readConfiguration(['--endpoint', args[1], ...mode, ...collection]), /collection-id/)
    }
  }
  assert.throws(() => readConfiguration(['--endpoint', args[1], '--collection-id', 'public-techniques']), /Entra GUID/)
  assert.throws(() => readConfiguration(args.map((value) =>
    value === args[5] ? '00000000-0000-0000-0000-000000000000' : value)), /Entra GUID/)
})

test('rejects duplicate or ambiguous options instead of silently choosing a value', () => {
  for (const option of ['endpoint', 'collection-id', 'tenant', 'client-id', 'api-client-id']) {
    assert.throws(() => readConfiguration([...args, `--${option}`, 'duplicate']), /must not be repeated/)
  }
  const anonymous = ['--endpoint', args[1], '--collection-id', 'curated-tools', '--anonymous']
  assert.throws(() => readConfiguration([...anonymous, '--anonymous']), /must not be repeated/)
  assert.throws(() => readConfiguration([...anonymous, '--anonymous=false']))
  assert.throws(() => readConfiguration([...anonymous, 'false']))
  assert.throws(() => readConfiguration([...anonymous, '--token', 'synthetic']))
})
