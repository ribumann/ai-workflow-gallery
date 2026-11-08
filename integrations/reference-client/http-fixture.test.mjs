import test from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { once } from 'node:events'
import { readConfiguration } from './configuration.mjs'
import { verifyConnection } from './client.mjs'

const readNames = ['search_tasks', 'get_task', 'search_workflows', 'get_workflow',
  'search_methods', 'get_method', 'search_reports', 'get_report']

for (const collectionId of ['public-techniques', 'curated-tools', 'playground']) {
  test(`real SDK and HTTP: explicit anonymous ${collectionId}`, { timeout: 10000 }, async () => {
    const calls = []
    const fixtureErrors = []
    const server = createServer(async (request, response) => {
      try {
        assert.equal(request.url, '/api/mcp') // Discovery and alternate paths fail the fixture.
        assert.equal(request.headers.authorization, undefined)
        assert.equal(request.headers.origin, undefined) // Nonbrowser access.
        assert.equal(request.headers['mcp-session-id'], undefined)
        if (request.method === 'GET') {
          response.writeHead(405, { Allow: 'POST, DELETE' }).end()
          return
        }
        if (request.method === 'DELETE') {
          response.writeHead(204).end()
          return
        }
        assert.equal(request.method, 'POST')
        assert.match(request.headers['content-type'], /application\/json/)
        const chunks = []
        for await (const chunk of request) chunks.push(chunk)
        const message = JSON.parse(Buffer.concat(chunks).toString())
        calls.push(message)
        let result
        switch (message.method) {
          case 'initialize':
            assert.equal(message.params.protocolVersion, '2025-11-25')
            result = {
              protocolVersion: message.params.protocolVersion,
              capabilities: { tools: { listChanged: false } },
              serverInfo: { name: 'deterministic-gallery-fixture', version: '1.0.0' },
            }
            break
          case 'notifications/initialized':
            response.writeHead(202).end()
            return
          case 'tools/list':
            result = { tools: readNames.map((name) => ({
              name,
              annotations: { readOnlyHint: true },
              inputSchema: {
                type: 'object',
                properties: {
                  collectionId: { type: 'string', enum: ['public-techniques', 'curated-tools', 'playground'] },
                  page: { type: 'integer', minimum: 1 },
                  pageSize: { type: 'integer', minimum: 1, maximum: 25 },
                },
                required: ['collectionId'],
              },
            })) }
            break
          case 'tools/call':
            assert.deepEqual(message.params, {
              name: 'search_tasks', arguments: { collectionId, page: 1, pageSize: 1 },
            })
            result = {
              content: [{ type: 'text', text: '{"items":[],"totalCount":0,"page":1,"pageSize":1}' }],
              structuredContent: { items: [], totalCount: 0, page: 1, pageSize: 1 },
              isError: false,
            }
            break
          default: assert.fail(`Unexpected method ${message.method}`)
        }
        response.writeHead(200, { 'content-type': 'application/json' })
          .end(JSON.stringify({ jsonrpc: '2.0', id: message.id, result }))
      } catch (error) {
        fixtureErrors.push(error)
        response.writeHead(500).end()
      }
    })
    server.listen(0, '127.0.0.1')
    await once(server, 'listening')
    try {
      const configuration = readConfiguration([
        '--endpoint', 'https://gallery.example/api/mcp', '--anonymous', '--collection-id', collectionId,
      ])
      // Test-only loopback endpoint: production CLI validation remains exact HTTPS.
      configuration.endpoint = new URL(`http://127.0.0.1:${server.address().port}/api/mcp`)
      const result = await verifyConnection(configuration, {
        authenticateImplementation: async () => assert.fail('Anonymous mode must never authenticate'),
      }) // No fetch mock: real SDK StreamableHTTPClientTransport and native HTTP fetch.
      assert.deepEqual(fixtureErrors, [])
      assert.deepEqual(calls.map((call) => call.method),
        ['initialize', 'notifications/initialized', 'tools/list', 'tools/call'])
      assert.equal(result.authMode, 'anonymous')
      assert.equal(result.collectionId, collectionId)
      assert.equal(result.initialized, true)
      assert.equal(result.taskSearchSucceeded, true)
      assert.equal(result.live, true) // Only this loopback run, never the public candidate.
      assert.deepEqual(result.tools, readNames)
    } finally {
      server.closeAllConnections()
      await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()))
    }
  })
}
