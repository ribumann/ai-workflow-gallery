import test from 'node:test'
import assert from 'node:assert/strict'
import { verifyConnection } from './client.mjs'
import { readConfiguration } from './configuration.mjs'

const endpoint = 'https://gallery.example/api/mcp'
const entra = ['--tenant', '11111111-1111-4111-8111-111111111111',
  '--client-id', '22222222-2222-4222-8222-222222222222',
  '--api-client-id', '33333333-3333-4333-8333-333333333333']

for (const anonymous of [true, false]) {
  for (const collectionId of ['public-techniques', 'curated-tools', 'playground']) {
    test(`bounded SDK read: ${anonymous ? 'anonymous' : 'Entra'}, ${collectionId}`, async () => {
      const configuration = readConfiguration([
        '--endpoint', endpoint, '--collection-id', collectionId, ...(anonymous ? ['--anonymous'] : entra),
      ])
      let authentications = 0
      let searches = 0
      const result = await verifyConnection(configuration, {
        authenticateImplementation: async () => { authentications++; return 'synthetic-test-token' },
        fetchImplementation: async (url, options) => {
          assert.equal(String(url), endpoint) // No discovery or other endpoint in anonymous mode.
          assert.equal(new Headers(options.headers).get('authorization'),
            anonymous ? null : 'Bearer synthetic-test-token')
          assert.equal(options.redirect, 'error')
          if (options.method === 'GET') return new Response(null, { status: 405 })
          if (options.method === 'DELETE') return new Response(null, { status: 204 })
          const message = JSON.parse(options.body)
          let result
          switch (message.method) {
            case 'initialize':
              result = {
                protocolVersion: message.params.protocolVersion,
                capabilities: { tools: { listChanged: false } },
                serverInfo: { name: 'gallery-test', version: '1' },
              }
              break
            case 'notifications/initialized': return new Response(null, { status: 202 })
            case 'tools/list':
              result = { tools: [{ name: 'search_tasks', inputSchema: { type: 'object' } }] }
              break
            case 'tools/call':
              searches++
              assert.deepEqual(message.params, {
                name: 'search_tasks', arguments: { collectionId, page: 1, pageSize: 1 },
              })
              result = { content: [{ type: 'text', text: '{"items":[]}' }] }
              break
            default: assert.fail(`Unexpected call ${message.method}`)
          }
          return new Response(JSON.stringify({ jsonrpc: '2.0', id: message.id, result }), {
            headers: { 'content-type': 'application/json' },
          })
        },
      })
      assert.equal(authentications, anonymous ? 0 : 1)
      assert.equal(searches, 1)
      assert.equal(result.authMode, anonymous ? 'anonymous' : 'entra')
      assert.equal(result.collectionId, collectionId)
      assert.equal(result.live, true)
    })
  }
}

test('failed private discovery never attempts anonymous MCP', async () => {
  const configuration = readConfiguration(['--endpoint', endpoint, '--collection-id', 'public-techniques', ...entra])
  let requests = 0
  await assert.rejects(verifyConnection(configuration, {
    fetchImplementation: async (url) => {
      requests++
      assert.equal(String(url), 'https://gallery.example/api/oauth-resource-metadata')
      return new Response(null, { status: 503 })
    },
  }), /discovery failed/)
  assert.equal(requests, 1)
})

test('failed private authentication never attempts anonymous MCP', async () => {
  const configuration = readConfiguration(['--endpoint', endpoint, '--collection-id', 'curated-tools', ...entra])
  await assert.rejects(verifyConnection(configuration, {
    authenticateImplementation: async () => { throw new Error('Consent denied') },
    fetchImplementation: async () => assert.fail('No MCP requests after private auth failure'),
  }), /Consent denied/)
  await assert.rejects(verifyConnection(configuration, {
    authenticateImplementation: async () => null,
    fetchImplementation: async () => assert.fail('No MCP requests without a delegated token'),
  }), /did not return a delegated token/)
})
