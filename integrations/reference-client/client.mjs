import { pathToFileURL } from 'node:url'
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js'
import { readConfiguration, verifyMetadata } from './configuration.mjs'
import { createResourceFetch } from './resource-fetch.mjs'

async function authenticate(configuration, fetchImplementation) {
  const discovery = await fetchImplementation(configuration.metadata, { redirect: 'error', signal: AbortSignal.timeout(15000) })
  if (!discovery.ok) throw new Error(`Resource discovery failed with HTTP ${discovery.status}.`)
  verifyMetadata(await discovery.json(), configuration)

  const { PublicClientApplication } = await import('@azure/msal-node')
  const application = new PublicClientApplication({
    auth: { clientId: configuration.clientId, authority: configuration.authority.replace('/v2.0', '') },
    // The default MSAL Node cache is in memory. Do not add a persistence plugin.
  })
  const authentication = await application.acquireTokenByDeviceCode({
    scopes: [configuration.scope],
    deviceCodeCallback: (response) => console.error(response.message),
  })
  if (!authentication?.accessToken) throw new Error('The identity provider did not return a delegated token.')
  return authentication.accessToken
}

export async function verifyConnection(configuration, {
  fetchImplementation = globalThis.fetch,
  authenticateImplementation = authenticate,
} = {}) {
  // Anonymous is an explicit choice, never a fallback after discovery or sign-in fails.
  const headers = {}
  if (!configuration.anonymous) {
    const token = await authenticateImplementation(configuration, fetchImplementation)
    if (!token) throw new Error('The identity provider did not return a delegated token.')
    headers.Authorization = `Bearer ${token}`
  }
  const transport = new StreamableHTTPClientTransport(configuration.endpoint, {
    fetch: createResourceFetch(configuration.endpoint, fetchImplementation),
    requestInit: { headers, redirect: 'error' },
  })
  const client = new Client({ name: 'gallery-reference-client', version: '1.0.0' })
  try {
    await client.connect(transport)
    const tools = await client.listTools()
    if (!tools.tools.some((tool) => tool.name === 'search_tasks')) throw new Error('Expected gallery tool is absent.')
    const result = await client.callTool({
      name: 'search_tasks', arguments: { collectionId: configuration.collectionId, page: 1, pageSize: 1 },
    })
    if (result.isError) throw new Error('The gallery task search returned a tool error.')
    return {
      resource: configuration.endpoint.href,
      authMode: configuration.anonymous ? 'anonymous' : 'entra',
      collectionId: configuration.collectionId,
      initialized: true,
      tools: tools.tools.map((tool) => tool.name),
      taskSearchSucceeded: true,
      live: true,
    }
  } finally {
    await client.close()
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  Promise.resolve().then(() => verifyConnection(readConfiguration()))
    .then((result) => console.log(JSON.stringify(result, null, 2))).catch((error) => {
      // Provider/SDK exceptions can contain HTTP details. Never dump a token-bearing response.
      console.error(error instanceof Error ? error.message : 'Reference client failed.')
      process.exitCode = 1
    })
}
