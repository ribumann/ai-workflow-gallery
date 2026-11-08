# AI Workflow Gallery

A shared catalog of AI tasks, workflows, reusable methods and report/dashboard links.
Tasks describe agents, skills, plugins, prompts and MCP tools; ordered workflows connect
them to outcomes. The browser and MCP server use the same handlers, validation,
collection-scoped authorization and concurrency rules.

This is a flexible open-source bootstrap app with a modest hosted sample, not a
production or large-scale enterprise deployment blueprint. Start with **one
environment**; additional independent environments are optional and use their own
configuration, identities and data resources.

The public profile has exactly three collections: **Public techniques** and **Curated
tools** are published catalogs with anonymous read-only access; **Playground** is shared, public and
disposable, with anonymous authoring available only after its explicit opt-in and
reviewed SQL/storage protections. Content in Playground must not contain sensitive
or private data. See the [Playground contract](docs/playground.md) and
[deployment guide](docs/public-demo-deployment.md).

## Authentication and scope

The private profile uses Entra delegated bearer authentication. Public-mode requests
remain anonymous and carry an explicit `collectionId`; anonymous access does not create
a user or impersonate an Entra identity. Collection capabilities are request-scoped,
and public/curated content remains protected from Playground writes.

Authentication is optional per deployment, not optional per request in the private
profile. Private deployments can explicitly enable `CollectionAuthorization` to map
validated Entra group object IDs or app-role values to collection read/write grants
within one identity tenant. Missing membership and unmapped collections are denied;
existing feature-specific permissions still apply. The private profile without that
opt-in retains its existing admitted-user permissions.

Choose one profile for each deployment; authentication is not a browser toggle:

| | Public catalog (authentication off) | Private gallery (authentication on) |
|---|---|---|
| Requests | Anonymous; catalog/data requests require an explicit `collectionId` | Protected API requests require a validated Entra delegated bearer token; OAuth discovery metadata is public |
| Collections | Public Techniques and Curated Tools are read-only; Playground writes remain off until separately approved | Existing private permissions; optionally add collection-specific Entra grants |
| Infrastructure | Fresh-install `infra/public-demo.bicep` through the reviewed `scripts/deploy-public-demo.ps1` preview/apply flow | Existing private stack in `infra/main.bicep` with deployment-specific Entra, SQL and network parameters |
| Guide | [Public demo deployment](docs/public-demo-deployment.md) | [Architecture and authorization](docs/architecture.md) |

These are separate configurations, not interchangeable flags on an existing deployment.
The public profile never falls back to private authentication, and a failed private
authentication request never becomes anonymous. Do not use the public-demo installer
to update an existing private gallery.

For the private profile, `infra/entra/registration.json` supplies the API audience,
scope and frontend client IDs; pass the admitted directory with `-AllowedTenantId`.
`infra/main.bicep` writes the API auth settings, and `deploy-environment.ps1` uses the
same registration for the frontend build. Set `galleryBaseUrl` to the exact public
origin when MCP clients use the Static Web App or a custom domain; otherwise OAuth
metadata advertises the direct Function App origin. Local frontend development uses
the same IDs in `frontend/.env.local`. Microsoft-tenant access is denied by default;
use `-AllowMicrosoftTenant` only when that tenant is the explicitly admitted tenant.

The authenticated configuration can admit users from organizations within one Azure/
identity tenant; collection ownership and grants are deployment-owned configuration.
Multiple organizations or collections do not require multiple Azure tenants or one
deployment per organization. This is not a self-service organization membership system:
customer membership administration and cross-tenant data isolation are not implemented.
See the [architecture and configuration boundaries](docs/architecture.md).

**Curated publication:** this release has no hosted publisher or publisher sign-in.
The owner supplies independently reviewed export/import packages. Public visitors
cannot publish curated records or write curated attachments; the source public
profile preserves dedicated curated reader storage. Ordinary authorized editing
in a separate private installation is not a public publication endpoint.

## Features

Discover searches tasks, workflows and methods with server-backed paging, filters and
URL-backed history. Tasks and workflows support files, optimistic concurrency and
explicit task/workflow/method relationships. Engagement and reports retain their
existing feature-specific permissions.

The **Workflow composer** produces a proposal, never a saved catalog item. A person
reviews the proposal and saves through the ordinary collection-scoped editor.

Public MCP remains opt-in and collection-bound. Published collections expose eight
read tools. When separately enabled with the reviewed data-plane protections, Playground
exposes those reads plus twelve existing authoring tools. Workflows are catalog
documents, not executable agents. See the [MCP guide](docs/connect-production-mcp-server.md).

## Local development

Use Node 24 and .NET 10 LTS (SDK selected by `global.json`). Commands below use Windows
native npm entrypoints; Linux/macOS use `npm` and `npx`.

```powershell
npm.cmd --prefix frontend ci
dotnet restore api\tests\WorkflowGallery.Api.Tests.csproj
dotnet tool restore
Copy-Item api\src\local.settings.json.template api\src\local.settings.json
Copy-Item frontend\.env.example frontend\.env.local
```

Populate local settings from your own API registration and development SQL/storage
configuration. Keep local credentials and environment-specific identifiers out of
tracked files. Provider sign-in and consent must be completed normally.

```powershell
# Separate terminals
Set-Location api\src
func start
```

```powershell
Set-Location frontend
npm.cmd run dev
```

## Verification

```powershell
node scripts\validate.mjs
```

Install prerequisites first: .NET 10 SDK, Node 24, Python 3, frontend/reference-client
locked dependencies and Playwright Chromium. The entry point never installs tools
automatically. It runs API, frontend, desktop/mobile browser and registered-client SDK
checks, stops on a failed command and preserves its exit code. Explicit `api`, `frontend`,
`browser` or `reference` selectors run only that named suite.

API validation builds the test project, writes a fresh TRX directory and rejects missing
reports/counters, failed tests or zero executed tests. Browser suites mock session/backend
responses; they do not prove live Entra authentication, Playground data-plane protection,
or deployed acceptance.

The [guided C4 architecture](docs/architecture.md) covers landscape, context,
containers, components, selected code structures, runtime flows and public/private/local
deployment, with supporting data and operations views. Deployment guidance and a
validation/evidence index are in [`docs/`](docs/).


## Public source release candidate

This history-free source snapshot is a release candidate, not an approved release.
The rights-holder-approved source LICENSE is preserved unchanged; the sanitizer
does not grant rights or approve publication. Existing third-party notices and public-source citations remain applicable;
an integration recipe does not grant access to a third-party service.

Deployment identities and host/resource names in the supplied examples are
fictional placeholders. Replace them using your own registrations and explicitly
chosen Azure target before deploying. Environment-specific deployment workflows,
identity bootstrap resources and trust artifacts are intentionally excluded;
ordinary build, test and Copilot setup workflows remain. No trust or access to
another environment is transferred.
