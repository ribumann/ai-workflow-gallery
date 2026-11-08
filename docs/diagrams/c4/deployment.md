# Deployment views

[Architecture tour](../../architecture.md) | [Next: data model](data.md)

**Learn:** deployment maps Level 2 containers onto machines, managed services and
network nodes. A private endpoint is a network interface in your VNet; Azure SQL
and Storage remain managed services outside it. VNet integration controls the
Function App's outbound path, not automatic private-only HTTP ingress.

The diagrams are source-template deployment views, followed by the observed
release differences. They deliberately do not assert template defaults equal
the retained live installations.

## Public source-template topology

```mermaid
flowchart TB
    subgraph device["User device"]
        browser["Web browser<br/>Container instance: SPA"]
    end
    mcp["Configured MCP client<br/>External device"]
    operator["Approved deployment / import operator<br/>External management process"]
    subgraph azure["Azure subscription / resource group - public installation"]
        swa["Static Web Apps Standard<br/>Node: static SPA assets / HTTPS origin"]
        func["Linux Flex Consumption Function App<br/>Node: .NET 10 API worker instance(s)<br/>Public HTTPS ingress; user-assigned identities"]
        subgraph vnet["Virtual network - outbound integration"]
            integration["snet-functions<br/>Delegated integration subnet"]
            endpoints["snet-privateendpoints<br/>SQL, host Blob, curated Blob, Playground Blob,<br/>optional model private endpoint NICs"]
            dns["Linked private DNS zones<br/>SQL / Blob / optional Cognitive Services"]
        end
        sql[("Azure SQL server / serverless database<br/>Container instance: catalog + budget")]
        host[("Runtime/general Blob account<br/>Host/package containers + configured general files")]
        curated[("Curated Blob account<br/>curated-files container")]
        playground[("Playground Blob account<br/>playground-files + cumulative budget blob<br/>Writer role conditional on approved opt-in")]
        model["Optional Foundry / Azure OpenAI account<br/>Model deployment, data-plane inference"]
        telemetry["Application Insights + Log Analytics<br/>Workspace-based monitoring"]
        identities["User-assigned identities<br/>runtime / curated reader / Playground files"]
    end
    browser -->|"HTTPS assets and same-origin /api"| swa
    swa -->|"linkedBackends: forwards /api"| func
    mcp -->|"HTTPS /api/mcp through canonical origin; opt-in"| swa
    operator -.->|"Reviewed ARM deployment / SQL import via approved private reachability"| azure
    func -->|"Outbound VNet integration"| integration
    integration -->|"Private endpoint addresses"| endpoints
    integration -->|"Resolve service hostnames"| dns
    endpoints -->|"TDS/TLS"| sql
    endpoints -->|"HTTPS Blob"| host
    endpoints -->|"HTTPS Blob"| curated
    endpoints -->|"HTTPS Blob"| playground
    endpoints -.->|"HTTPS inference when provisioned"| model
    identities -.->|"Attached identities select data-plane principal"| func
    func -->|"Worker telemetry"| telemetry
```

### Identity and network layers

| Principal / node | Source-template relationship | Boundary |
|---|---|---|
| Runtime identity | Function host/package Blob role; explicit managed-identity SQL connection; conditional OpenAI User role | Runtime operations, SQL grants and inference; SQL permission is separately provisioned, not inferred from ARM RBAC |
| Curated reader identity | Blob Data Reader scoped to `curated-files` in its own account | Curated file reads only; API also refuses isolated curated upload/delete |
| Playground file identity | Container-scoped reader, conditional Blob Data Contributor | Does not reuse curated credentials; writes require separate protections |
| Private endpoints + DNS | SQL, each configured Blob account, optional model | Data-plane network reachability; not user authentication or SQL privilege |
| Public Function ingress | Explicitly enabled in public Function module | Worker policy remains authoritative even if caller bypasses SWA |
| Operator | Approved Azure access plus SQL principal and private network reachability | Management/import access is not given to anonymous runtime users |

Template public deployment uses Sweden Central for runtime/data and East US 2
for the SWA resource. The Function template allows up to 40 instances of 2048 MB
with no always-ready instances. Those are source settings, not measured capacity
or a production SLA. Storage accounts and SQL disable public data access; the
model account, when provisioned, uses a private endpoint and keyless inference.
NSG association parameters do not create an egress firewall or its rules.

Model capacity provisioning does not itself enable public Composer. Complete
settings, inference access and the SQL budget ledger are independent
prerequisites. Playground account provisioning does not itself activate writes.
The public installer is a fresh-install preview/apply flow; it is not a safe
update tool for an existing private installation.

## Private source-template topology

```mermaid
flowchart TB
    browser["User browser<br/>Container instance: private SPA"]
    client["MCP client<br/>External device: opt-in"]
    entra["Microsoft Entra ID<br/>SPA/API registrations and admitted directory"]
    subgraph azure["Azure resource group - separate private installation"]
        swa["Static Web Apps<br/>Node: private-profile SPA files + linked backend"]
        func["Linux Flex Consumption Function App<br/>Node: .NET 10 API worker<br/>Source template: system-assigned identity"]
        subgraph network["Private VNet - outbound integration"]
            functions["snet-functions<br/>Delegated integration"]
            endpoints["snet-privateendpoints<br/>SQL / Blob / model private endpoint NICs"]
            dns["Linked private DNS zones"]
        end
        sql[("Azure SQL database<br/>Container instance: chosen schema family")]
        storage[("Blob account<br/>Host/package and private task file containers")]
        model["Foundry / Azure OpenAI<br/>Optional runtime use; template provisions account"]
        acs["ACS + Email service<br/>Template resources; runtime opt-in"]
        monitor["Application Insights / Log Analytics"]
    end
    browser -->|"MSAL delegated provider flow"| entra
    browser -->|"HTTPS /api + API bearer"| swa
    client -->|"HTTPS MCP + delegated bearer"| swa
    swa -->|"linkedBackends"| func
    func -->|"Issuer / signing metadata; HTTPS"| entra
    func -->|"Outbound integration"| functions
    functions -->|"Private resolution"| dns
    functions -->|"Private endpoint traffic"| endpoints
    endpoints -->|"TDS/TLS + SQL-authorized managed identity"| sql
    endpoints -->|"HTTPS + Blob role"| storage
    endpoints -->|"HTTPS + OpenAI User role when enabled"| model
    func -.->|"Approved/configured feedback HTTPS; not private endpoint in this template"| acs
    func -->|"Diagnostics / audit"| monitor
```

An API token and a managed identity are different principals. The token says
which human may perform an operation; the managed identity lets the already
authorized worker access its backing services. Neither browser nor MCP client
receives the worker's SQL/Blob identity.

`infra/main.bicep` provisions model and communication resources even when their
runtime features are disabled; do not read an account's presence as evidence of
inference or email readiness. Always-ready HTTP instances are explicitly
parameterized; the private source Function maximum is 100 instances. These
defaults do not override the live UAT's retained settings.

## Observed release deployment differences

| Concern | Public gallery at release | Retained private UAT at release |
|---|---|---|
| Purpose | Anonymous public catalog | Original authorized private gallery |
| Logical installation count | One | One; no communication with public SQL |
| Frontend | Public-profile SPA; polished desktop/mobile | Matching private-profile SPA; same layout corrections |
| Worker/schema | Public catalog-schema source family | Original-schema .NET 10 recovery worker; not interchangeable with public worker |
| Authentication | Anonymous capabilities | Restored delegated identity profile; user sign-in/read confirmed |
| Playground | Writes off; dedicated authoring stack not installed there | Not converted to the disposable sample profile |
| Composer | Proposal-only | Separately configured private feature |
| SQL idle timeout | Do not infer from UAT or this table | 10080 minutes (one week), database Online at final release check |
| Runtime identity | Deployment-specific; do not infer from source diagram alone | Original user-assigned runtime identity retained, despite generic private template's system-assigned identity |
| Feedback | Delivery deferred | Delivery not an acceptance claim for this release |

The temporary disposable sample used for one-byte attachment acceptance was
retired. Its successful lifecycle does not mean public-domain Playground
authoring is enabled, nor that all public-template identities/stores exist
on that domain.

**Known source/live distinction:** `infra/main.bicep`'s private entrypoint
parameter `sqlAutoPauseDelayMinutes` does not allow 10080, while the underlying
SQL module does support it and the live UAT setting was separately applied.
Some template parameter descriptions still refer to historical startup seeding.
Current `Program.cs` verifies the catalog schema and does not run automatic
migrations or seeding. These are documented distinctions, not changes made by
this architecture task.

### Release installation map

```mermaid
flowchart LR
    publicUser["Anonymous visitor<br/>Public browser SPA instance"]
    privateUser["Admitted private member<br/>Private browser SPA instance"]
    entra["Entra provider<br/>Private delegated sign-in"]
    subgraph public["Retained public installation"]
        publicSwa["Public SWA<br/>Public gallery HTTPS origin"]
        publicApi["Public Function App<br/>Catalog-schema API worker<br/>All collection writes off"]
        publicData["Configured public catalog dependencies<br/>Separate from private UAT data"]
    end
    subgraph private["Retained private UAT installation"]
        privateSwa["Private SWA<br/>UAT HTTPS origin"]
        privateApi["Private Function App<br/>Original-schema .NET 10 recovery worker<br/>Original runtime identity"]
        privateSql[("Original private SQL database<br/>One-week idle timeout")]
        privateBlob[("Original private Blob storage")]
    end
    publicUser -->|"HTTPS"| publicSwa
    publicSwa -->|"Linked /api backend"| publicApi
    publicApi -->|"Configured data access"| publicData
    privateUser -->|"Provider sign-in"| entra
    privateUser -->|"HTTPS + delegated API token"| privateSwa
    privateSwa -->|"Linked /api backend"| privateApi
    privateApi -->|"Original SQL connection"| privateSql
    privateApi -->|"Original file storage connection"| privateBlob
```

This observation-level map intentionally aggregates public dependencies instead
of presenting every optional source-template resource as deployed. There is no
third sample installation and no cross-installation data path. The detailed
source-template diagrams above explain the available network/identity topology;
they are not a fresh ARM/RBAC inventory.

## Local development deployment

```mermaid
flowchart LR
    subgraph machine["Developer workstation - local environment boundary"]
        browser["Browser<br/>SPA instance"]
        vite["Vite development server<br/>Node: frontend assets / API proxy"]
        functions["Functions Core Tools host<br/>.NET 10 isolated worker instance"]
        sql[("Configured local SQL Server<br/>Catalog database")]
        azurite[("Configured local Azurite Blob service<br/>Optional attachment store")]
        importer["Import CLI process<br/>Explicit local target / actor"]
    end
    entra["Configured Entra application registrations<br/>Private provider sign-in"]
    model["Configured model endpoint<br/>Optional external inference"]
    browser -->|"Development HTTP origin"| vite
    vite -->|"Development /api proxy"| functions
    browser -.->|"Private profile: delegated sign-in"| entra
    functions -->|"Configured SQL connection"| sql
    functions -.->|"Development storage connection"| azurite
    functions -.->|"Private metadata validation"| entra
    functions -.->|"Explicitly configured inference"| model
    importer -->|"Reviewed local import"| sql
```

This is a local arrangement, not an installer that silently provisions those
services. .NET SDK comes from `global.json`; Node/dependencies come from the
documented development prerequisites and lockfiles. Local credential mechanisms
differ from managed identities in Azure. Never reuse production data or publish
local settings. `--initialize-catalog` refuses remote SQL and requires a new empty
local database.

## Build, release and deployment plane

GitHub build/test jobs validate source; they do not constitute Azure data-plane
acceptance. SPA assets and API packages are built/deployed separately. The API
package lives in Functions deployment Blob storage; SWA hosts built static assets.
Frontend-only releases preserve the linked backend. ARM templates describe
resources, while SQL schema/grants/import are separately reviewed operations.
The public source intentionally excludes environment-specific deployment
identities/workflows: downloading it transfers no cloud access.

**Evidence:** [public entrypoint](../../../infra/public-demo.bicep),
[public worker](../../../infra/modules/public-demo-function.bicep),
[private entrypoint](../../../infra/main.bicep),
[private worker](../../../infra/modules/function-app.bicep),
[network](../../../infra/modules/network.bicep),
[private endpoints](../../../infra/modules/private-endpoint.bicep),
[SQL](../../../infra/modules/sql.bicep),
[storage](../../../infra/modules/public-demo-storage.bicep),
[model](../../../infra/modules/ai-foundry.bicep),
[development proxy](../../../frontend/vite.config.ts),
[release evidence boundaries](../../playground-validation.md),
[deployment runbook](../../public-demo-deployment.md).
