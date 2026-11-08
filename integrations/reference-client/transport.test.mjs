import test from 'node:test'
import assert from 'node:assert/strict'
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js'
import { createResourceFetch } from './resource-fetch.mjs'

const endpoint = new URL('https://gallery.example/api/mcp')
// This is the gallery contract, not the SDK's version list: SDK upgrades must negotiate with it.
const galleryVersions = ['2026-07-28', '2025-11-25', '2025-06-18', '2025-03-26']

function jsonResponse(id, result) {
  return new Response(JSON.stringify({ jsonrpc: '2.0', id, result }), {
    headers: { 'content-type': 'application/json' },
  })
}

function initialize(message, supported = galleryVersions) {
  const requested = message.params.protocolVersion
  return jsonResponse(message.id, {
    protocolVersion: supported.includes(requested) ? requested : galleryVersions[0],
    capabilities: { tools: { listChanged: false } },
    serverInfo: { name: 'ai-workflow-gallery', version: '1.0.0' },
  })
}

function clientAndTransport(fetchImplementation) {
  const client = new Client({ name: 'gallery-sdk-regression', version: '1.0.0' })
  const transport = new StreamableHTTPClientTransport(endpoint, {
    fetch: createResourceFetch(endpoint, fetchImplementation),
    requestInit: { headers: { Authorization: 'Bearer synthetic-test-value' } },
  })
  return { client, transport }
}

test('real SDK initializes, lists tools and performs a bounded read with the gallery protocol', async () => {
  const calls = []
  const { client, transport } = clientAndTransport(async (url, options) => {
    const message = options.body ? JSON.parse(options.body) : undefined
    calls.push({ url: String(url), options, message })
    assert.equal(options.redirect, 'error')
    if (options.method === 'GET') return new Response(null, { status: 405 })
    if (message.method === 'initialize') return initialize(message)
    if (message.method === 'notifications/initialized') return new Response(null, { status: 202 })
    if (message.method === 'tools/list') {
      return jsonResponse(message.id, {
        tools: [{ name: 'search_tasks', inputSchema: { type: 'object' } }],
      })
    }
    if (message.method === 'tools/call') {
      assert.deepEqual(message.params, { name: 'search_tasks', arguments: { collectionId: 'public-techniques', page: 1, pageSize: 1 } })
      return jsonResponse(message.id, { content: [{ type: 'text', text: '{"items":[]}' }] })
    }
    throw new Error(`Unexpected MCP method: ${message.method}`)
  })
  try {
    await client.connect(transport)
    assert.equal(calls[0].message.params.protocolVersion, '2025-11-25')
    assert.equal((await client.listTools()).tools[0].name, 'search_tasks')
    assert.notEqual((await client.callTool({
      name: 'search_tasks', arguments: { collectionId: 'public-techniques', page: 1, pageSize: 1 },
    })).isError, true)
    const notification = calls.find((call) => call.message?.method === 'notifications/initialized')
    assert.equal(new Headers(notification.options.headers).get('mcp-protocol-version'), '2025-11-25')
    assert.ok(calls.every((call) => call.url === endpoint.href))
  } finally {
    await client.close()
  }
})

test('SDK rejects the previous unsupported fallback instead of claiming initialized', async () => {
  const { client, transport } = clientAndTransport(async (_url, options) => {
    if (options.method === 'GET') return new Response(null, { status: 405 })
    const message = JSON.parse(options.body)
    return initialize(message, ['2026-07-28', '2025-06-18', '2025-03-26'])
  })
  try {
    await assert.rejects(client.connect(transport), /protocol version is not supported: 2026-07-28/)
  } finally {
    await client.close()
  }
})

test('SSE GET cannot override redirect rejection or forward bearer to another path', { timeout: 5000 }, async () => {
  const calls = []
  const getAttempted = Promise.withResolvers()
  const { client, transport } = clientAndTransport(async (url, options) => {
    calls.push({ url: String(url), options })
    if (options.method === 'GET') {
      getAttempted.resolve()
      assert.equal(options.redirect, 'error')
      assert.equal(new Headers(options.headers).get('authorization'), 'Bearer synthetic-test-value')
      return new Response(null, { status: 302, headers: { location: '/login' } })
    }
    const message = JSON.parse(options.body)
    if (message.method === 'initialize') return initialize(message)
    if (message.method === 'notifications/initialized') return new Response(null, { status: 202 })
    throw new Error(`Unexpected MCP method: ${message.method}`)
  })
  const redirectError = Promise.withResolvers()
  client.onerror = (error) => redirectError.resolve(error)
  try {
    await client.connect(transport)
    await getAttempted.promise
    assert.match((await redirectError.promise).message, /redirects are not permitted/)
    assert.ok(calls.every((call) => call.url === endpoint.href))
  } finally {
    await client.close()
  }
})

test('resource binding rejects another path, authority or query before sending any request', async () => {
  let calls = 0
  const fetch = createResourceFetch(endpoint, async () => { calls++; return new Response() })
  for (const target of ['https://gallery.example/login', 'https://other.example/api/mcp',
    'https://gallery.example/api/mcp?token=synthetic']) {
    await assert.rejects(fetch(target), /unverified resource/)
  }
  assert.equal(calls, 0)
})
