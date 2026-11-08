export function createResourceFetch(endpoint, fetchImplementation = globalThis.fetch) {
  const resource = endpoint.href
  return async (input, options = {}) => {
    const target = new URL(input instanceof Request ? input.url : input)
    if (target.href !== resource) throw new Error('MCP request targets an unverified resource.')
    const response = await fetchImplementation(input, { ...options, redirect: 'error' })
    if (response.status >= 300 && response.status < 400) throw new Error('MCP resource redirects are not permitted.')
    return response
  }
}
