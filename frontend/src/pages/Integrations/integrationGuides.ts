import { branding } from '../../branding'

export type IntegrationStatus = 'available' | 'preview' | 'blocked'
export interface StarterPrompt { title: string; prompt: string }
export interface GuideSection { id: string; heading: string; body: string }
export interface IntegrationGuide {
  id: string
  label: string
  path: string
  tagline: string
  status: IntegrationStatus
  statusLabel: string
  statusDetail: string
  sections: (origin: string) => GuideSection[]
  prompts: StarterPrompt[]
  promptsNote?: string
}

const prompts: StarterPrompt[] = [
  { title: 'Find an existing approach', prompt: 'Search the gallery for tasks, workflows and methods about [topic]. Read the best matches and cite their gallery links.' },
  { title: 'Explain a workflow', prompt: 'Open [workflow] from the gallery. Explain its steps and prerequisites before running anything.' },
  { title: 'Reuse a prompt', prompt: 'Find a gallery task for [problem]. Show me how you would adapt its prompt before using it.' },
  { title: 'Contribute safely', prompt: 'Search for duplicates of [approach]. Draft a task or improvement and show it to me before saving.' },
]

const readiness: GuideSection = {
  id: 'readiness',
  heading: 'Check deployment readiness first',
  body: `**MCP availability is deployment-specific.** Ask the owner to confirm that MCP is enabled,
provide its exact canonical HTTPS endpoint, and state which authentication modes and collection
capabilities are active. Those instructions are not proof of live MCP access. Do not infer
readiness from this page's origin, a browser sign-in, a package download or a configured client.

Choose anonymous public access or private Entra explicitly. Published collections remain
read-only; Playground authoring is available only when its separate write policy and reviewed
SQL/storage protections are active.`,
}

function oauth(origin: string): GuideSection {
  return {
    id: 'oauth',
    heading: 'Prerequisites for an existing enabled MCP server',
    body: `The endpoint candidate for this gallery is **${origin}/api/mcp**; confirm it with the owner.
Every tool call in **both modes** requires \`collectionId\`, exactly \`public-techniques\`,
\`curated-tools\` or \`playground\`. There is no default collection or global union; do not add it to the endpoint URL.

**Anonymous public read-only:** use explicit \`--anonymous --collection-id public-techniques\`
(or \`curated-tools\`). No Entra discovery, token or dummy GUID. The published collections expose
only the eight existing read tools. Playground discovery adds the twelve existing catalog
authoring tools only when the owner confirms the separate write opt-in and reviewed SQL/storage
protections and PublicPlaygroundWritesEnabled=true. EnableMcpWrites=true alone never enables
public writes. Content is shared, public and disposable. The maintained reference client remains
read-verification only.
Never fall back to anonymous after failed private discovery/authentication.

**Private Entra:** backend discovery is at **${origin}/api/oauth-resource-metadata**, available through direct
Functions and SWA endpoints. SWA also provides
**${origin}/.well-known/oauth-protected-resource/api/mcp** as an alias.

Use an authorized repository checkout, PowerShell 5+, Node.js 24+ and npm. For private mode the deployment owner
must supply a **preregistered public client ID**, admitted directory GUID and API application GUID.
Request \`api://<api-client-id>/Gallery.Access\` on your own behalf.
The API validates the token; browser sign-in alone does not authorize an MCP client.

Owner prerequisites are separate: legitimate canonical HTTPS identifier registration, v2 tokens,
enabled local service principals, permission/consent and tenant admission. These are not end-user
installation steps. This guide contains no deployment, grant or enablement command.

Entra does not provide arbitrary dynamic client registration here. Native GitHub Copilot,
Microsoft 365, Scout and Cowork compatibility requires separate documented and live evidence.
Use the repository's \`integrations/reference-client\` for the supported reference flow; it does
not configure a native assistant. Anonymous mode also makes no automatic native compatibility claim.
Conditional Access/admin-consent denial is a **stop condition**.

**Public server contract:** all three flags are required: \`PublicDemoEnabled=true\`,
\`EnableMcpServer=true\` AND \`EnablePublicMcp=true\`. Public MCP remains opt-in/default-off.
Stateless POST JSON Streamable HTTP;
GET 405, DELETE 204, no SSE/session state. A supplied Origin must exactly match an explicit HTTPS
origin in comma-separated \`PublicMcpAllowedOrigins\`; nonbrowser requests without Origin work.
Fixed limits: actual streamed request body **64 KiB** (not just Content-Length), serialized response
**512 KiB**, pageSize maximum **25** (oversized pages error, no silent truncation), **20 seconds**,
**2 concurrent requests**, **60 admissions per rolling minute per worker**. Admission rejection:
**429 with Retry-After**. Not a global guarantee; no shared ledger or inference service.

Do not configure a static Authorization header containing a token. Do not copy browser session
credentials. Complete sign-in/consent only through the identity provider. Multi-tenant registration
does not grant access: your directory must also be admitted by this deployment.`,
  }
}

function introduction(origin: string, path: string): GuideSection {
  return {
    id: 'assistant',
    heading: 'Copy/paste self-install instruction',
    body: `Paste this into your selected assistant and replace the bracketed fields. This is a
manual instruction, not one-click setup. No installation or sign-in has happened.

\`\`\`text
Help me use ${branding.title} at ${origin}.
Guide: ${origin}${path}
Environment: [name]; client/product and version: [selected client].
Repository checkout: [local path].
Auth mode: [anonymous public read-only OR private Entra].
Collection: [public-techniques OR curated-tools OR playground]; include collectionId in every tool call.
Private only: owner directory GUID [GUID]; public client GUID [GUID]; API GUID [GUID].
Confirm the environment and whether MCP is enabled. The page origin is only a
candidate; use the owner's exact canonical endpoint, not an assumed alias.
If browser/API-only or 503 mcp_disabled, stop MCP setup and use the authorized
browser/skill route. Installing a skill alone does not connect tools.
Inspect the selected client's documented capabilities; do not invent native config,
dynamic registration, a static Authorization header or compatibility.
Show proposed local files, dependency installation and commands for review BEFORE
modifying configuration or installing anything. Wait for my approval.
Do not deploy/enable MCP, register apps, grant access or change DNS/policy.
Use the reviewed repository reference client if native compatibility is unverified.
Anonymous: explicit --anonymous, no Entra discovery, token or dummy GUID.
Private only: Pause for me to complete provider sign-in/consent. Never copy my tokens.
Never fall back to anonymous after failed private discovery/authentication.
Stop on Conditional Access, admin-consent or directory-admission failure.
Verify only initialize, tools/list and one bounded task search; report actual results.
Reference-client success is not proof of native tools in this assistant session.
Do not write gallery content. Explain removal of only local config you added,
not shared/cloud config or consent.
\`\`\``,
  }
}

function verification(origin: string): GuideSection {
  return {
    id: 'verify',
    heading: 'Copy/paste the reference-client verification',
    body: `Only for an already enabled server: from the **repository root**, review locked dependencies
and replace the quoted nonsecret placeholders. Confirm the endpoint with the owner first.
The block installs local reference-client dependencies; it does not deploy or enable a server.

**Anonymous public read-only** (no Entra discovery or sign-in):

\`\`\`powershell
& {
    $ErrorActionPreference = 'Stop'
    $endpoint = '${origin}/api/mcp'
    $collectionId = 'public-techniques' # Explicitly choose this or curated-tools.
    if (-not (Test-Path '.\\integrations\\reference-client\\package-lock.json')) {
        throw 'Run this block from the gallery repository root.'
    }
    $nodeMajor = node -p "process.versions.node.split('.')[0]"
    if ($LASTEXITCODE -ne 0 -or [int]$nodeMajor -lt 24) { throw 'Node.js 24 or later is required.' }
    npm.cmd --prefix .\\integrations\\reference-client ci
    if ($LASTEXITCODE -ne 0) { throw 'Reference-client dependency installation failed.' }
    node .\\integrations\\reference-client\\client.mjs --endpoint $endpoint --anonymous --collection-id $collectionId
    if ($LASTEXITCODE -ne 0) { throw 'Reference-client verification failed; stop and inspect the error.' }
}
\`\`\`

**Private Entra** (choose this instead of anonymous for a private deployment):

\`\`\`powershell
& {
    $ErrorActionPreference = 'Stop'
    $endpoint = '${origin}/api/mcp'
    $collectionId = 'public-techniques' # Explicitly choose this or curated-tools.
    $tenantId = '<admitted-directory-guid>'
    $publicClientId = '<registered-public-client-guid>'
    $apiClientId = '<api-application-guid>'

    if (-not (Test-Path '.\\integrations\\reference-client\\package-lock.json')) {
        throw 'Run this block from the gallery repository root.'
    }
    $nodeMajor = node -p "process.versions.node.split('.')[0]"
    if ($LASTEXITCODE -ne 0 -or [int]$nodeMajor -lt 24) {
        throw 'Node.js 24 or later is required.'
    }
    npm.cmd --prefix .\\integrations\\reference-client ci
    if ($LASTEXITCODE -ne 0) { throw 'Reference-client dependency installation failed.' }
    node .\\integrations\\reference-client\\client.mjs --endpoint $endpoint --tenant $tenantId --client-id $publicClientId --api-client-id $apiClientId --collection-id $collectionId
    if ($LASTEXITCODE -ne 0) { throw 'Reference-client verification failed; stop and inspect the error.' }
}
\`\`\`

In private mode the maintained MSAL/MCP SDK client first checks same-origin metadata, rejecting redirects and
requiring the exact resource, directory authority and API scope. Only then complete the provider's
displayed sign-in instruction **yourself**. Tokens stay in memory and are never saved or emitted.

Both modes initialize MCP, list tools and call \`search_tasks\` with explicit \`collectionId\` and \`page: 1, pageSize: 1\`.
Require exit code 0 and the chosen resource, \`authMode\`, \`collectionId\`, \`initialized: true\`, \`taskSearchSucceeded: true\`
and \`live: true\`. Empty search results are allowed. This proves only that reference run, not
native-client compatibility or writes. It never mutates gallery content.`,
  }
}

const skill: GuideSection = {
  id: 'skill',
  heading: 'Teach your assistant when to use the gallery',
  body: `Download [the skill folder](/downloads/ai-workflow-gallery-skill.zip), or the
[SKILL.md file](/downloads/ai-workflow-gallery-SKILL.md) with its
[gallery map](/downloads/ai-workflow-gallery-gallery-map.md) and
[MCP tool reference](/downloads/ai-workflow-gallery-mcp-tools.md).

Keep the two references in a \`references\` folder, removing the download filename prefix.
Review your client's documented skill installation and account/license requirements first.
The skill uses connected MCP tools or your authorized browser; it grants no access itself.
Search before creating, show drafts before saving, and never retry a write with unknown delivery.`,
}

const troubleshooting: GuideSection = {
  id: 'troubleshooting',
  heading: 'Troubleshooting',
  body: `| Result | Meaning |
|---|---|
| 503 / mcp_disabled | Stop MCP setup; use the authorized browser/skill route |
| Other 503 or metadata mismatch | Stop; owner checks auth/discovery and canonical resource, not another host |
| 401 / bearer challenge | Sign-in/token is absent, invalid or expired; use the registered provider flow |
| 403 | Stop; directory admission or delegated permission denied |
| Public 403 with Origin | Owner checks explicit exact HTTPS PublicMcpAllowedOrigins; no inferred allowlist |
| 429 / Retry-After | Respect the per-worker limit and wait; no automatic retry loop or target switch |
| Conditional Access / admin consent / AADSTS650052 | Stop; owner reviews policy, consent or service-principal prerequisites |
| Only read tools | Expected for published collections; Playground write tools require its separate capability and database/storage policy |
| HTML or a redirect | Wrong endpoint or an incompatible gateway; this API never redirects MCP to login |

No automatic write replay occurs. After uncertain delivery or a network failure, inspect the gallery before deciding
whether to repeat a mutation. Client registration and consent do not prove a live tool call;
verify initialize, tools/list and an actual read. Closing the reference process discards its in-memory tokens.
For separately approved native setup, review/remove only the local config entry you added; this does not
revoke cloud consent. No destructive cleanup command is supplied.`,
}

export const integrationGuides: IntegrationGuide[] = [
  {
    id: 'github-copilot', label: 'GitHub Copilot', path: '/integrations/github-copilot',
    tagline: 'Manual setup for an enabled server; browser/skill fallback is separate.',
    status: 'preview', statusLabel: 'Client verification required',
    statusDetail: 'The reference-client flow does not establish native GitHub Copilot compatibility or MCP readiness for a deployment.',
    sections: (origin) => [
      readiness, oauth(origin), introduction(origin, '/integrations/github-copilot'), verification(origin),
      skill, troubleshooting,
    ],
    prompts,
  },
  {
    id: 'cowork', label: 'Microsoft 365 Copilot Cowork', path: '/integrations/cowork',
    tagline: 'Use the plugin where organizational policy allows; the authorized browser is always the fallback.',
    status: 'preview',
    statusLabel: branding.publisher ? 'Plugin package available' : 'Browser fallback available',
    statusDetail: branding.publisher
      ? 'The package contains a skill, not a connector. Package availability does not establish organizational approval or account entitlement. Use the authorized browser if import is unavailable or blocked.'
      : 'Plugin package publication is not configured for this deployment. Organizational policy may further restrict installation; this status does not establish that it is permitted. The authorized browser remains available as the fallback.',
    sections: (origin) => [
      readiness, oauth(origin), introduction(origin, '/integrations/cowork'),
      { id: 'package', heading: 'Option A — Review the Cowork plugin package', body: branding.publisher
        ? 'Download the [Cowork plugin package](/downloads/ai-workflow-gallery-cowork-plugin.zip). It bundles the gallery skill and references; it does not include a connector or OAuth credentials. Before importing it, check your account entitlement and organizational app/package policy; administrator approval may be required. Package availability does not guarantee tenant acceptance. If import is unavailable or blocked, use the browser fallback below.'
        : 'Plugin package publication is not configured for this deployment. The owner must supply a publisher identity and real website, privacy and terms URLs in the public branding configuration before building the package. The browser fallback below remains available.' },
      { id: 'browser', heading: 'Option B — Use the authorized browser (always available)', body: `Where your account/client supports custom skills, review and install the [skill folder](/downloads/ai-workflow-gallery-skill.zip) through its documented flow. Open ${origin} in the browser you authorize it to use. This browser route remains available when plugin import is unavailable or blocked by organizational policy. Complete any sign-in/consent yourself; the assistant must not extract credentials. This is separate from MCP setup.` },
      verification(origin),
      { id: 'limits', heading: 'MCP connector compatibility is not yet verified', body: 'A connector must be separately verified for the selected anonymous read-only transport or registered delegated Entra client, permission, consent and resource metadata contract. Neither anonymous access nor OAuth discovery means arbitrary cloud clients can connect. Private access does not support a shared user identity or application-only gallery access.' },
      skill, troubleshooting,
    ],
    prompts,
  },
  {
    id: 'scout', label: 'Microsoft Scout', path: '/integrations/scout',
    tagline: 'Check access and client compatibility; do not enable anything through this guide.',
    status: 'preview', statusLabel: 'Client compatibility unverified',
    statusDetail: 'A URL or custom-header dialog is not proof of anonymous transport or delegated OAuth compatibility; Scout access and native support require independent evidence.',
    sections: (origin) => [readiness, oauth(origin), introduction(origin, '/integrations/scout'), verification(origin), skill, troubleshooting],
    prompts,
  },
]

export function findIntegrationGuide(id: string | undefined) {
  return integrationGuides.find((guide) => guide.id === id)
}
