# Connect to an existing gallery MCP server

The filename is retained for existing links. This is an organization-neutral client
guide, **not a deployment script**. One deployment can expose the three scoped public
collections documented below; this does not implement customer or organization tenancy.

## Current status: confirm the selected deployment

MCP availability, authentication mode and collection capabilities are deployment-specific.
Ask the owner to confirm that MCP is enabled, provide its exact canonical HTTPS endpoint,
and state whether Playground writes are active. Do not infer readiness from a page origin,
historical hostname, skill or package download.

## Choose authentication explicitly

The **public MCP** contract is separate from private Entra MCP. Use only the exact endpoint
and status confirmed by the deployment owner; this guide does not claim that any endpoint
is deployed or that Playground writes are active.

Every tool call in **both modes** requires `collectionId`, exactly
`public-techniques`, `curated-tools` or `playground`. There is no default collection or global
union. For public MCP, the transport query selects the connection profile and every tool call
must repeat that same collection in its arguments; the endpoint remains `/api/mcp`.

- **Anonymous published read:** explicitly pass `--anonymous --collection-id public-techniques`
  (or `curated-tools`). This retains exactly the existing eight read tools.
- **Anonymous Playground authoring:** only when the owner confirms the dedicated Playground
  write switch and reviewed SQL/storage protections are active, select
  `--anonymous --collection-id playground`. Discovery exposes the eight reads and twelve existing
  catalog create/update/delete tools. The maintained reference client is read-verification only;
  it does not execute writes. Ask the person to approve each proposed change before calling a
  write tool. Content is shared, public and disposable.
- **Private Entra:** omit `--anonymous`, supply the registered identifiers below and
  `--collection-id public-techniques` (or `curated-tools`). Discovery or authentication
  failure is a stop condition, **never an automatic fallback to anonymous**.

### Public reference-client verification

After confirming the exact endpoint and reviewing locked dependencies, from the repository root:

```powershell
& {
    $ErrorActionPreference = 'Stop'
    $endpoint = 'https://<owner-confirmed-public-host>/api/mcp'
    $collectionId = 'public-techniques' # Explicitly choose this or curated-tools.
    if (-not (Test-Path '.\integrations\reference-client\package-lock.json')) {
        throw 'Run this block from the gallery repository root.'
    }
    $nodeMajor = node -p "process.versions.node.split('.')[0]"
    if ($LASTEXITCODE -ne 0 -or [int]$nodeMajor -lt 24) { throw 'Node.js 24 or later is required.' }
    npm.cmd --prefix .\integrations\reference-client ci
    if ($LASTEXITCODE -ne 0) { throw 'Reference-client dependency installation failed.' }
    node .\integrations\reference-client\client.mjs --endpoint $endpoint --anonymous --collection-id $collectionId
    if ($LASTEXITCODE -ne 0) { throw 'Reference-client verification failed; stop and inspect the error.' }
}
```

The public `public-techniques` and `curated-tools` connections expose only the eight existing
read tools: `search_tasks`, `get_task`,
`search_workflows`, `get_workflow`, `search_methods`, `get_method`, `search_reports`,
`get_report`. Writes remain unavailable on both published connections, regardless of `EnableMcpWrites`.
The Playground connection exposes twelve additional existing catalog authoring tools only when
`PublicPlaygroundWritesEnabled=true`; it reuses the same handlers and collection validation.
No native-client compatibility follows from anonymous access or reference-client success.

### Public transport and fixed bounds

Published read-only MCP requires **all three**: `PublicDemoEnabled=true`,
`EnableMcpServer=true` and `EnablePublicMcp=true`. Public MCP remains opt-in/default-off;
this guide does not enable it. Playground MCP writes additionally require
`PublicPlaygroundWritesEnabled=true`, reviewed database-side protections, the scoped SQL grants
and Playground storage role; `EnableMcpWrites` alone never activates public writes.
Transport is stateless POST JSON Streamable HTTP (no SSE/session state);
GET returns 405, DELETE returns 204. A supplied `Origin` must exactly match an explicit
HTTPS origin in comma-separated `PublicMcpAllowedOrigins`; no wildcard or inferred origin.
Nonbrowser requests without `Origin` work; CORS is not authentication.

Fixed public limits: actual streamed request body **64 KiB** (not just Content-Length),
serialized response **512 KiB**, `pageSize` maximum **25** (oversized pages error, never
silently truncate), **20 seconds**, **2 concurrent requests**, and **60 admissions per
rolling minute per worker**. Admission rejection returns **429 with Retry-After**.
These are per-worker controls, not a global cost/rate guarantee; there is no shared ledger
or inference service. On overload, respect Retry-After; do not loop or switch targets.

## End user: prerequisites

- For **private Entra**, an **already enabled** server and an owner-supplied canonical
  `https://<gallery-host>/api/mcp`, admitted directory GUID, preregistered public
  client GUID and API application GUID. These identifiers are nonsecret.
- An authorized repository checkout, PowerShell 5 or later, Node.js 24+ and npm.
  Run the commands from the **repository root**; review locked dependencies first.
- Your own provider-managed sign-in and supported consent. Conditional Access or
  administrator-consent denial is a **stop condition**, not permission to bypass policy.

The maintained MSAL Node/MCP SDK reference client is the supported reference flow.
It is not a native GitHub Copilot, Microsoft 365, Scout or Cowork connector.
Those clients require separate documented and live evidence for the exact
registration/resource/permission flow; no generic compatibility is promised.

## End user: copy/paste reference-client setup

Replace the quoted placeholders with values from the deployment owner and choose a collection.
This block installs locked **local reference-client dependencies** and runs a bounded
read. It does not deploy/enable MCP, register an app, grant access, change DNS or sign
in on your behalf. Run it only against the exact endpoint confirmed by its owner.

```powershell
& {
    $ErrorActionPreference = 'Stop'
    $endpoint = 'https://<gallery-host>/api/mcp'
    $collectionId = 'public-techniques' # Explicitly choose this or curated-tools.
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

### Provider sign-in and bounded verification

In private mode only, after verifying metadata the client displays provider-managed device-code
sign-in instructions. Complete them **yourself** with the intended account. Never
paste credentials/tokens into chat, static Authorization headers or client config;
do not extract browser tokens. Browser SPA sign-in is a separate PKCE flow.

The client checks same-origin `/api/oauth-resource-metadata` with redirects rejected.
Its `resource` must equal the exact selected `/api/mcp` URL; authority must match
the chosen directory and scope must be `api://<api-application-guid>/Gallery.Access`.
SWA also exposes `/.well-known/oauth-protected-resource/api/mcp` as an alias;
the reference client uses the explicit `/api/oauth-resource-metadata` route on both
SWA and direct Functions. Another hostname is not an automatic fallback.

After sign-in the client performs MCP initialize, `tools/list`, and one
`search_tasks` call with the explicit `collectionId` and `page: 1, pageSize: 1`. An empty result can be a successful
read. Require exit code 0 and output showing the chosen `resource`,
`initialized: true`, `taskSearchSucceeded: true` and `live: true`. This proves that
reference run only, not a native assistant connection or write access. Tokens stay
in memory; no token cache file is created.

## Copyable assistant self-install instruction

Replace the bracketed fields; paste into the assistant you intend to use. This is
manual instruction, **not one-click automatic setup**. The assistant must review
before modifying local configuration and pause for human authorization/consent.

```text
Help me connect to my selected AI Workflow Gallery:
Gallery: [canonical HTTPS gallery URL]; environment: [environment name].
Guide: [gallery URL]/integrations/[github-copilot, cowork or scout].
Repository checkout: [local path]. Client/product and version: [selected client].
Auth mode: [anonymous public read-only OR private Entra].
Collection: [public-techniques OR curated-tools OR playground]; include collectionId in every tool call.
Private only: owner directory GUID [GUID]; public client GUID [GUID]; API GUID [GUID].

Read this deployment's guide and confirm the exact environment and MCP readiness.
If it is browser/API-only or returns 503 mcp_disabled, stop MCP setup. Offer the
authorized browser/skill route; downloading a skill does not enable tools.
Inspect the selected client's documented capabilities; do not invent client config,
dynamic registration, native compatibility or a static Authorization header.
Show the proposed local files, dependency install and commands for review BEFORE
modifying configuration or installing anything. Wait for my approval.
Do not deploy/enable a server, register apps, grant permissions or change DNS/policy.
Use the reviewed repository reference client if native compatibility is unverified.
Anonymous: use --anonymous, no Entra discovery, token or dummy GUID.
Private only: pause for me to complete provider sign-in/consent; never copy my tokens.
Never fall back to anonymous after failed private discovery/authentication.
Stop on Conditional Access, admin-consent or directory-admission failure.
Verify only initialize, tools/list and one bounded task search. Report actual results
and distinguish reference-client success from native tools in this assistant session.
Do not create/edit/delete gallery content. Explain how to remove only the local
configuration you added; do not remove shared/cloud configuration or revoke consent.
```

The in-app guides tailor this instruction to the selected gallery origin and surface.
Skill and package links stay available there as a separate browser/instruction route.

## Deployment owner: prerequisites, not user installation

Owner deployment work requires separate approval. This guide deliberately supplies
no registration, grant, deployment, DNS or enablement commands.

- Set the API to issue v2 tokens and expose delegated `Gallery.Access`. Its token
  audience is the API client GUID. The exact canonical HTTPS MCP URL must be a
  legitimate registered identifier of that same API application; keep the existing
  `api://<api-application-guid>/Gallery.Access` scope aligned with it.
- Resolve canonical-host/verified-domain and tenant policy constraints first.
  A v2 setting alone is not evidence that a multitenant HTTPS identifier is accepted.
  Do not switch audiences, invent an identifier, broaden account access or bypass policy.
- Ensure enabled local `Application` service principals for the API, SPA and
  reference client, bound to the reviewed client IDs/home tenant. A registration
  alone is insufficient; owner bootstrap and admission are separate authorization.
- The reference client needs public-client/device-code support, the correct delegated
  permission and supported consent. Device code can be disallowed by policy.
  This flow does not use arbitrary dynamic client registration (DCR).
- Configure MCP availability separately from writes. `EnableMcpWrites` is an
  independent gate; public metadata grants no tool/data access. Browser and MCP
  handlers in private mode retain authenticated identity and validation rules.
  Public/Curated connections remain read-only. Playground writes require its separate
  opt-in plus reviewed SQL/storage protections; `EnableMcpWrites` alone never enables
  them. In private mode, show the exact draft/impact and obtain human confirmation
  before every write; never replay a mutation with uncertain delivery.

See [Entra MCP guidance](https://learn.microsoft.com/en-us/entra/agent-id/secure-mcp-server-with-entra-id),
[identifier URI restrictions](https://learn.microsoft.com/en-us/entra/identity-platform/identifier-uri-restrictions)
and [multitenant identifier guidance](https://learn.microsoft.com/en-us/entra/identity-platform/howto-convert-app-to-be-multi-tenant).

## Troubleshooting and local cleanup

| Result | Next action |
|---|---|
| JSON 503 / `mcp_disabled` | Stop MCP setup; use authorized browser/API access. Only the owner can review a separately approved full-MCP release. |
| Other 503 or metadata mismatch | Stop; owner checks auth/discovery configuration and exact canonical resource. Do not try another host automatically. |
| 401 / bearer challenge | Use the registered client's supported provider sign-in; do not copy browser credentials. |
| 403 | Stop; owner checks directory admission and delegated permission. |
| Public 403 with Origin | Owner checks explicit exact HTTPS PublicMcpAllowedOrigins; do not infer an allowlist or switch identities. |
| 429 / Retry-After | Per-worker admission limit; wait as directed, no automatic retry loop. |
| Conditional Access / admin consent / `AADSTS650052` | Stop; request owner review of policy, consent or service-principal prerequisites. |
| HTML/redirect | Wrong endpoint or gateway; MCP does not redirect to browser login. |
| Reference success, no assistant tools | Native connection is still unverified; configuration or skill files alone prove nothing. |

Closing the reference process discards its in-memory tokens. If an approved native
setup later adds local configuration, review and remove only that specific local
entry/folder when no longer wanted; that does not revoke cloud consent. No cleanup
command is executed by these instructions. Never replay a mutation with uncertain
delivery; the reference verification does not make any mutations.
