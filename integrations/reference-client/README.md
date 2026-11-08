# Gallery MCP reference client

This maintained MSAL Node/MCP SDK client connects to an **existing enabled** server.
It does not deploy, register apps, grant access, change DNS, invent DCR or configure a
native assistant. The [connection guide](../../docs/connect-production-mcp-server.md)
separates end-user steps from owner prerequisites and includes a copyable
review-before-install assistant instruction.

## Prerequisites

Choose the mode explicitly: **anonymous public** or **private Entra**. Ask the deployment
owner for the exact endpoint and current readiness; published-read acceptance does not
prove that Playground writes are active.
See the [connection guide](../../docs/connect-production-mcp-server.md#public-transport-and-fixed-bounds)
for the public enablement gates, Origin policy and fixed per-worker limits.

Both modes require `--collection-id public-techniques`, `--collection-id curated-tools` or
`--collection-id playground`. This client performs a bounded read-only verification even when
the selected deployment enables Playground authoring.
No default/global collection is inferred. Every `search_tasks` verification supplies
that `collectionId` in the tool arguments; it is not part of the endpoint URL.
Repeated CLI options and mixing anonymous mode with Entra options are rejected,
not silently resolved to the last value.

Use PowerShell 5+, Node.js 24+ and npm from an authorized repository checkout.
Review the locked dependencies. For private Entra, the owner must supply the exact canonical HTTPS
`/api/mcp` endpoint, admitted directory GUID, preregistered public client GUID and
API application GUID. The public client must support device code, request the API's
delegated permission and obtain supported provider-managed consent. Registrations
and enabled local service principals are owner prerequisites, not user setup.

Conditional Access, administrator consent and directory admission are independent
gates. A denial is a stop condition; do not change policy or use another identity as
a workaround. Browser sign-in uses a separate SPA/authorization-code PKCE flow.

## Copy/paste from the repository root

For private Entra, replace the quoted nonsecret placeholders before running. The block installs
only local locked reference-client dependencies, then performs bounded read verification.

```powershell
& {
    $ErrorActionPreference = 'Stop'
    $endpoint = 'https://<gallery-host>/api/mcp'
    $collectionId = 'public-techniques' # Explicitly choose public-techniques, curated-tools or playground.
    $tenantId = '<admitted-directory-guid>'
    $publicClientId = '<registered-public-client-guid>'
    $apiClientId = '<api-application-guid>'

    if (-not (Test-Path '.\integrations\reference-client\package-lock.json')) {
        throw 'Run this block from the gallery repository root.'
    }
    $nodeMajor = node -p "process.versions.node.split('.')[0]"
    if ($LASTEXITCODE -ne 0 -or [int]$nodeMajor -lt 24) {
        throw 'Node.js 24 or later is required.'
    }
    npm.cmd --prefix .\integrations\reference-client ci
    if ($LASTEXITCODE -ne 0) { throw 'Reference-client dependency installation failed.' }
    node .\integrations\reference-client\client.mjs --endpoint $endpoint --tenant $tenantId --client-id $publicClientId --api-client-id $apiClientId --collection-id $collectionId
    if ($LASTEXITCODE -ne 0) { throw 'Reference-client verification failed; stop and inspect the error.' }
}
```

For an owner-confirmed public server, use the same local prerequisite and
dependency checks, then **instead of** the private command run:

```powershell
node .\integrations\reference-client\client.mjs --endpoint $endpoint --anonymous --collection-id $collectionId
if ($LASTEXITCODE -ne 0) { throw 'Reference-client verification failed; stop and inspect the error.' }
```

Explicit `--anonymous` skips Entra discovery and authentication entirely; no token or
dummy GUID is needed. It rejects Entra flags combined with anonymous mode. It never
falls back from failed private discovery or authentication to public access.
Public techniques and Curated tools expose only the eight existing read tools. Playground
adds the twelve existing authoring tools only when its separate write switch and reviewed
SQL/storage protections are active; `EnableMcpWrites=true` alone never enables them. Private
writes require separate human draft/impact confirmation; this reference client only reads in
either mode.

In private mode the client first verifies same-origin `/api/oauth-resource-metadata` with redirects
rejected. It requires the exact canonical resource, selected directory authority and
`api://<api-application-guid>/Gallery.Access` scope. SWA's
`/.well-known/oauth-protected-resource/api/mcp` alias is not used by this client;
the `/api/oauth-resource-metadata` route works on SWA and direct Functions.
Another origin is not a registration or fallback. Entra v2 token audience is the
API client GUID, not an arbitrary endpoint URL.

Complete the provider's displayed device-code sign-in **yourself**. Do not put
tokens in static Authorization headers/configuration, copy browser credentials or
paste tokens into chat. MSAL's default cache stays in memory; no persistence plugin
or credential-bearing config is created. Closing the process discards that cache.

## What success proves

The client initializes MCP, lists tools and calls `search_tasks` with the explicit `collectionId` and `page: 1,
pageSize: 1`. Empty results are allowed. Require exit code 0 and JSON containing
the selected `resource`, `authMode`, `collectionId`, `initialized: true`, `taskSearchSucceeded: true` and
`live: true`. It never mutates gallery content. It does not verify native GitHub
Copilot, Microsoft 365, Scout or Cowork compatibility or access to write tools.

503 / `mcp_disabled` means stop and use the authorized browser/skill route. Other
503/metadata errors need owner review. 401 needs supported sign-in; 403 means
admission/permission denial. Conditional Access/admin-consent failures are not
client repair opportunities. See the guide's troubleshooting table.

## Local checks and cleanup

From the repository root, after dependencies are present:

```powershell
& {
    npm.cmd --prefix .\integrations\reference-client test
    if ($LASTEXITCODE -ne 0) { throw 'Reference-client tests failed.' }
}
```

These tests cover configuration/resource binding, invalid/ambiguous options, no private
auth downgrade and transport safety, **not live sign-in**. A deterministic loopback HTTP
fixture exercises anonymous initialize/list/search through the actual SDK transport and
native fetch for both collections, asserting no discovery requests or Authorization header.
That fixture is not a live public-deployment check. Separate release evidence establishes the
deployed published-read base, not Playground write acceptance. If a
separately approved native-client setup later adds local config, review/remove only
that entry; removing it does not revoke cloud consent. This reference flow writes
no client config, and no destructive cleanup command is supplied.
