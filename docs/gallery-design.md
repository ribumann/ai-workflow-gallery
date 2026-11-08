# Gallery design and extensibility

**Status: approved fresh-fork catalog core.**
Public-source coverage reviewed 5 November 2026. This is a small design contract, not
an implementation schedule or a claim of verified live gallery connections.

The approved fresh fork replaces the old classification enums; it does not migrate,
coerce or reset an existing database. The executable definitions are in
[`catalog/registry.json`](../catalog/registry.json). Frontend controls, API validation,
Composer grounding and MCP discovery use that same reviewed registry. See
[catalog contracts](catalog-core.md) for wire and storage initialization details.
The historical proposal below explains the distinctions behind those definitions;
its original documentation-only authorization and legacy migration language no longer
apply to this approved source implementation.

## Current implementation and boundaries

The gallery has Tasks, ordered Workflows, Methods, report/app links and authenticated
attachments. Its React frontend uses Fluent UI v9; the API uses Entra delegated identity.
The historical task types were Agent, Skill, Plugin, Prompt and McpTool, with one
M365Copilot, GitHubCopilot, Scout or Cowork surface per task. The fresh core replaces
those enums with the shared registry's kind, explicit targets and optional format.

The fresh core supports explicit collection scopes in one deployment and one Azure
tenant. Collection containers are not a multitenancy or organization-administration
platform. Authenticated access remains the private default. The separate public profile
has two published, anonymous read-only collections and an opt-in shared, disposable
Playground; its capabilities do not alter private Entra authorization. The Workflow composer generates a
proposal and never saves it automatically.

MCP readiness is deployment-specific. The public and private connection guides do not
claim that a particular deployment has enabled MCP or Playground authoring.
Documentation, a downloaded skill or a package does not enable MCP.

## Proposed minimum model

Use **one reviewed, flat registry/data file** for external targets.
Each target has a stable namespaced ID, display label and aliases; provider, product
and host are labels on that record, not separate services or tables. Record supported
artifact **kind + format**, short prerequisites and dated public-source annotations.
Only register sourced combinations; an absent combination is unknown, not supported.

For example, `github.copilot.cli` is a target, `skill` is a kind and
`agentskills.skill-md` is a format. IDs survive display-name changes. Aliases are searchable labels, not alternate IDs
accepted in writes; unknown IDs are rejected. This avoids silently rewriting stored
classification or guessing a replacement target.
Keep three facts distinct in plain language:

- **Documented compatibility:** evidence for this artifact/format and target.
- **Access prerequisites:** license, preview enrollment, account and administrator policy.
- **Gallery readiness:** whether the selected deployment/client has a verified connection.

No complex status model is needed. A public support statement or a preview license
does not prove a live gallery connection. A provider-specific package does not become
portable because another provider also calls something a plugin or skill.

Typed task metadata and its eventual storage/API representation remain a future
decision. Legacy `M365Copilot` and `McpTool` values stay **ambiguous/unverified**
until reviewed; do not infer a host, or silently turn a server into a tool. Preserve
IDs, blobs, content, routes/bookmarks, concurrency tokens, attribution and workflow
live-reference/deletion-snapshot behavior in any later migration.

Do not introduce a mandatory API v2, collection-wide 409 adapter, database-backed
product directory, dependency graph, generated form engine, native export service
or brand administration product. Normal content can describe dependencies for now.

## Dated offering map

This map defines coverage to document, not a list of working gallery integrations.
Verify the linked product's current rollout and license/admin requirements before
adding a target. Sources are public first-party documentation; no private-preview
material or internal compatibility certification is implied.

| Family | Distinctions to preserve | Public source |
|---|---|---|
| Copilot Chat / Microsoft 365 Copilot | Experience, grounding and license are separate. Do not equate browser chat with every licensed work feature. | [Copilot overview](https://learn.microsoft.com/en-us/copilot/plan/copilot-overview) |
| Microsoft 365 apps and workspaces | Distinguish Word, Excel, PowerPoint, Outlook, Teams, OneNote and Edge interaction hosts from SharePoint/OneDrive data and Loop, Pages or Notebooks workspaces/output. Not every extension works in every app. | [Extensibility overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview), [Pages/Notebooks requirements](https://learn.microsoft.com/en-us/microsoft-365/loop/cpcn-requirements?view=o365-worldwide) |
| Agents | Built-in experiences such as Researcher/Analyst are references, not invented downloadable definitions. Declarative agents and custom-engine agents have different authoring/runtime contracts. | [Extensibility overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview) |
| Plugin packages | A package can contain agents, skills, prompts/instructions or connection resources. Package format and host support must be documented separately from each contained artifact. | [Plugins overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugins-overview) |
| Microsoft Copilot Cowork | Describe documented skills, plugins, connectors and scheduled/event-driven work with feature-specific account/access prerequisites. Automation templates are instructions, not a gallery scheduler. | [Cowork overview](https://learn.microsoft.com/en-us/copilot/cowork/) |
| Microsoft Scout / Frontier | Scout is a documented desktop experience. Frontier is an access program, **not a runtime** or proof of tenant availability. | [Scout overview](https://learn.microsoft.com/en-us/microsoft-scout/overview) |
| Copilot Code | Keep this separately documented experience distinct from Scout and GitHub Copilot; retain its documented Frontier/access qualifications. | [Copilot Code](https://learn.microsoft.com/en-us/copilot/code/) |
| Connectors / tools | Synced/indexed and federated/query-time Copilot connectors differ. Power Platform connectors are another contract. Distinguish an MCP server from an individual tool it exposes. | [Copilot connectors](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview-copilot-connector), [development tools](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/choose-plugin-development-tools) |
| GitHub Copilot | IDE, app, CLI and cloud are distinct targets. Source support per surface for instructions, prompt files, custom agents, skills, hooks and MCP; do not extrapolate one surface's support to another. | [Customization cheat sheet](https://docs.github.com/en/copilot/reference/customization-cheat-sheet) |

Builders and governance products (Agent Builder, Copilot Studio/Power Platform,
Agents Toolkit/SDK and Agent 365) are not automatically execution hosts. Hooks and
automation templates belong only where their target has a documented contract.
The map is deliberately not an exhaustive SKU inventory.

### Five sample classifications

These are classification examples, not converted assets or tested installations.

| Example | Kind + format | Target/context and qualification |
|---|---|---|
| Copyable writing prompt | Prompt + plain text | Copilot Chat; user supplies context and checks grounding/license needs. No connector implied. |
| Declarative-agent definition | Agent + Microsoft declarative-agent manifest | A documented Microsoft 365 host; keep manifest/schema and entitlement requirements. Not a custom-engine runtime. |
| Repository skill folder | Skill + SKILL.md folder | An explicitly documented GitHub Copilot surface; assess the actual skill and dependencies, not just its filename. |
| Cowork distribution ZIP | Plugin package + Microsoft-specific manifest | Contains skill/reference resources; the gallery package declares no connector. Import depends on account entitlement and organizational policy. The authorized browser remains the fallback if plugin use is unavailable or blocked. Downloadability is not tenant installation evidence. |
| Gallery task search reference | MCP tool + MCP tool schema | `search_tasks` is one tool on the gallery MCP server, not the server itself. Readiness is confirmed per deployment. |

**Short extension examples:** a future `openai.codex.cli` target could annotate a
documented SKILL.md profile using [OpenAI's skills guidance](https://learn.chatgpt.com/docs/build-skills).
A future `anthropic.claude-code.cli` target could annotate its instructions, skills,
hooks or provider-specific plugin profile using [Anthropic's overview](https://code.claude.com/docs/en/features-overview).
These are registry examples only: no live integration, automatic conversion, native
export or portable plugin format is promised. Microsoft Copilot Cowork and Anthropic
Cowork are not interchangeable products.

## Implemented neutral deployment identity

One validated [public build configuration](../frontend/README.md#deployment-branding)
now supplies neutral app/title text, optional
organization name, local logo/favicon with accessible text, and an accessible accent
mapped to Fluent tokens. Include optional contact/legal/source links, sharing choices
and packaged publisher identity. Defaults work without an organization's logo;
absent optional links are omitted rather than falling back to the original operator.

This foundation removes publisher-specific/Microsoft-as-operator branding, not accurate Microsoft
product/vendor names, Entra/SDK/schema identifiers, licenses or historical attribution.
Preserve history and user-authored content. The same reviewed public values should
reach generated packages, not just the shell. Invalid configuration stops the build.
Cowork publication requires explicit complete publisher/legal URLs; otherwise its ZIP
is omitted and the guide states that publication is unconfigured. Skill downloads remain
available. Schema IDs, download URLs for emitted files and skill identity are preserved.
Credentials, feedback recipients and tenant/admission policy are **not** public browser
branding configuration. Existing authentication boundaries remain unchanged.

## Fluent UI brief: branding refinement only; classification changes planned

The approved Impeccable simplicity assessment applies to the incumbent Fluent source,
not a replacement redesign. The branding slice preserves that implementation; the
classification controls below are still planned. Local mocked browser checks do not
establish live authentication, deployment acceptance or tenant package installation.

Preserve Fluent v9 theme/tokens, sidebar, mobile drawer and current Tasks/Workflows/Methods
discovery. The core classification controls are **Type** and **Works in**. Works in
should be searchable; format/setup fields are contextual and optional. Keep proof,
license guidance and secondary filters outside the required core form. Retain normal
labels, keyboard operation, validation, focus, semantic colors and wrapped long text.
Do not add a provider dashboard, decorative rewrite or generated wizard.

Manual/review workflow steps have instructions and expected outcomes, not a fictitious
product. Future catalog-backed steps select a real target. Keep the existing ordered
workflow rather than introducing an execution engine.

Integration guide order is **current status -> prerequisites -> user copy/paste ->
provider sign-in -> bounded read verification -> troubleshooting**. Separate client
connection from owner deployment prerequisites. Reuse the current renderer/packages.
The [MCP guide](connect-production-mcp-server.md) implements this instruction-first
documentation approach now; it does not install a client or enable a service.

Any later application change needs separate approval and must preserve download URLs/
aliases and existing data/authentication behavior. Its acceptance should include real
desktop/mobile, keyboard and error/loading/empty-state review before claiming UI readiness.
