# Gallery MCP tools

Companion reference for the `ai-workflow-gallery` skill. Load this when the gallery's MCP tools are
connected and you need the exact tool names and arguments.

If these tools are not available, ignore this file and use `references/gallery-map.md` to work
through the website instead.

## Before anything else: are you actually connected?

A skill folder on disk and an MCP connection are different things, and neither implies the other.
Check the tools you have in **this session** — if `search_tasks` is not one of them, you are not
connected, no matter what any config file contains.

Two config files that are commonly mistaken for proof:

| File | What it actually proves |
|---|---|
| `~/.scout/m-skills/ai-workflow-gallery/` | Scout has the *instructions*. Nothing about tools. |
| `~/.copilot/mcp-config.json` | Local **GitHub Copilot CLI** configuration exists. It proves neither reachability nor Scout compatibility. |

Always state the explicitly selected deployment URL/environment; never substitute a historical
endpoint. MCP readiness is deployment-specific. If MCP is disabled or its status is unknown,
stop MCP setup and use the authorized browser route. Installing this skill does not change
readiness.
An explicit JSON `503` with `error: mcp_disabled` means the selected server has MCP disabled;
ask its owner for the supported browser or connection path.

### Explicit anonymous public read-only mode

Use only the exact MCP endpoint confirmed by the deployment owner. A successful published
read does not prove that Playground writes are active. Confirm the selected environment and
current Playground write status with the owner.
Use `integrations/reference-client` with `--anonymous --collection-id public-techniques`,
`--anonymous --collection-id curated-tools` or `--anonymous --collection-id playground`.
Anonymous mode needs no Entra discovery,
token or dummy GUID. Never fall back to it after private discovery/authentication fails.
Reference success does not imply native Scout/Copilot/Cowork compatibility.

Every tool call in **both auth modes** requires `collectionId`, exactly
`public-techniques`, `curated-tools` or `playground`. It is an argument, not an endpoint query parameter;
there is no default collection or global union. For example:

```json
{"name":"search_tasks","arguments":{"collectionId":"public-techniques","page":1,"pageSize":1}}
```

Public MCP requires `PublicDemoEnabled=true` AND `EnableMcpServer=true` AND
`EnablePublicMcp=true`. Published collections provide the eight read tools below.
Playground adds the twelve existing authoring tools only when `PublicPlaygroundWritesEnabled=true`
and reviewed SQL/storage protections are active. `EnableMcpWrites=true` alone never enables
public writes. Playground content is shared, public and disposable; the maintained reference client
performs bounded read verification only.
It is stateless POST JSON Streamable HTTP: GET 405, DELETE 204, no SSE.
Supplied Origin must exactly match an explicit HTTPS origin in comma-separated
`PublicMcpAllowedOrigins`. Nonbrowser requests without Origin work.

Fixed public bounds: actual streamed body 64 KiB (not only Content-Length), serialized
response 512 KiB, pageSize maximum 25 (oversized pages error, no silent truncation),
20 seconds, 2 concurrent requests and 60 admissions per rolling minute **per worker**.
Admission rejection returns 429 with Retry-After. Respect it; do not loop or switch targets.
These are not global guarantees; there is no shared ledger or inference service.

### Registered delegated OAuth

The endpoint is `<chosen-origin>/api/mcp`; protected-resource discovery is at
`<chosen-origin>/api/oauth-resource-metadata` on direct Functions and SWA endpoints.
SWA also provides `<chosen-origin>/.well-known/oauth-protected-resource/api/mcp` as an alias.
Use an explicitly registered Entra public client, admitted directory, and
`api://<api-client-id>/Gallery.Access` delegated scope. Do not copy browser credentials
or write access tokens into configuration.

The repository's `integrations/reference-client` verifies discovery/resource/scope and uses
maintained MSAL plus the MCP SDK to initialize, list tools and search. Tokens stay in memory.
Pass `--collection-id public-techniques`, `--collection-id curated-tools` or
`--collection-id playground` here too.
Native Scout/Copilot/Cowork compatibility is a separate verification; a custom-header UI
or generic dynamic-registration implementation is not proof of compatibility.
Installing the skill does not register a client, obtain consent or grant directory admission.
Only connect to an existing enabled deployment. Use the repository's
`docs/connect-production-mcp-server.md` for owner prerequisites and copy/paste instructions.
Review proposed local configuration/dependency installation before changes; pause for human
provider sign-in/consent. Conditional Access, administrator-consent and admission failures
are stop conditions, not permission to change policy. Reference-client success does not
prove that native tools are available in this assistant session.

## Reading

| Tool | Purpose |
|---|---|
| `search_tasks` | Keyword search across tasks |
| `get_task` | Open one task by id |
| `search_workflows` | Keyword search across workflows |
| `get_workflow` | Open one workflow by id |
| `search_methods` | Keyword search across AI methods |
| `get_method` | Open one AI method by id |
| `search_reports` | Keyword search across report and dashboard links |
| `get_report` | Open one report link by id |

## Writing (private mode or explicitly enabled Playground)

Published public collections cannot write. Public Playground writes are limited to that
shared, disposable collection and require the separate owner-confirmed opt-in and database/
storage protections. In private mode, existing permissions remain in force. In either mode,
confirm the exact draft/impact with the human before every write; tool visibility never
substitutes for approval.

| Tool | Purpose |
|---|---|
| `create_task` / `update_task` / `delete_task` | Tasks |
| `create_workflow` / `update_workflow` / `delete_workflow` | Workflows |
| `create_method` / `update_method` / `delete_method` | AI methods |
| `create_report` / `update_report` / `delete_report` | Report and dashboard links |

## Behaviour that differs from an ordinary REST API

Four things will surprise you if you assume REST semantics:

- **Omitting a field leaves it alone.** `update_task { id, name }` changes the name and nothing
  else. To *clear* an optional field, pass an empty string. An empty string in a **required** field
  (`name`, `description`, `title`, `summary`, `approach`, `url`, `problemStatement`,
  `desiredOutcome`) is a validation error, not a clear.
- **Delete gates differ by entry type.** An unreferenced `delete_task` can delete immediately;
  referenced tasks return an impact report and require `confirm: true` after the person has
  seen it and agreed. `delete_workflow`, `delete_method` and `delete_report` always require
  `confirm: true` after approval of the reported impact. A referenced method still cannot be
  deleted after confirmation: remove its task/workflow references first, with approval.
  Workflow deletion removes its steps, not their tasks; report deletion removes only the
  gallery link, not its destination. Never rely on a server gate instead of the user's approval.
- **Pass the concurrency token back.** Send the `lastEditedAt` (tasks, workflows) or `version`
  (methods, reports) that the matching `get_*` tool returned. Someone else's concurrent edit then
  surfaces as a conflict instead of silently overwriting their work. For `update_task` this matters
  most: omit it and you get last-write-wins with no guard at all.
- **No file uploads.** Attachments and images have to go through the website.

## Working practice

- Always `search_*` before any `create_*`. Duplicates are the main way a catalog decays.
- Read the result of `get_*` before proposing an edit — do not edit from the search summary alone.
- Show the user the arguments you intend to send to any `create_*` or `update_*` tool, and wait for
  their approval before calling it.
-   Private calls are attributed to the signed-in user by name. Anonymous Playground catalog
  changes use the reserved sandbox attribution actor and do not establish a signed-in identity.

## If the tools stop working

401 requires the registered client's supported sign-in flow. 403 is an admission/permission
denial; 503 means authentication is unavailable. HTML/redirects indicate the wrong endpoint
or gateway, not this API's sign-in protocol. Never automatically replay a write after an
uncertain response. Use the Tool integration guide; do not extract browser credentials.

If the tools were never there, connection/readiness is unverified, not proof of an
expired token or a need for Entra registration in anonymous mode. Go back to
"Before anything else: are you actually connected?".
