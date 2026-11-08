# Level 3: components

[Architecture tour](../../architecture.md) | [Next: code](code.md)

**Learn:** components describe responsibility inside a container. Three diagrams
zoom into the three application containers from Level 2. SQL and provider boxes
remain outside those boundaries. A component grouping can span several files;
it does not imply a separate package, process or service.

## Browser SPA

```mermaid
flowchart LR
    subgraph browser["Browser SPA - container boundary"]
        routes["App / React Router / AppShell<br/>Component: navigation and theme"]
        auth["AuthProvider / RequireAuth / entraClient<br/>Component: session and delegated token lifecycle"]
        pages["Search, detail, editor, report pages<br/>Component group: catalog interactions"]
        proposal["AiComposerPanel<br/>Component: goal, proposal review and explicit editor handoff"]
        integrations["IntegrationGuidePage<br/>Component: instructions and package downloads"]
        client["apiClient + service types<br/>Component: typed shared HTTP boundary"]
        deployment["deployment.ts / collection capability helpers<br/>Component: profile and selected collection"]
        url["URL state utilities<br/>Component: browser history and discovery state"]
    end
    api["API worker<br/>Container"]
    entra["Entra<br/>External system"]
    routes -->|"Mounts / guards routes"| auth
    routes -->|"Renders"| pages
    routes -->|"Renders"| integrations
    pages -->|"Reads / writes via"| client
    pages -->|"Manages discovery state"| url
    pages -->|"Requests / reviews proposal"| proposal
    proposal -->|"Calls proposal endpoint"| client
    client -->|"Acquires private bearer"| auth
    client -->|"Adds collection scope"| deployment
    auth -->|"Bootstraps / checks session"| client
    auth -->|"MSAL provider flow; private profile"| entra
    client -->|"HTTPS /api"| api
```

The AuthProvider/client arrows describe runtime collaboration, not a new token
service. Build-time public mode omits token acquisition; the server must still
confirm the matching public capability. Editor routes and controls hide writes
for read-only capabilities, but only the API can authorize a mutation.
Reauthentication preserves mounted drafts and does not replay failed requests.

## API worker

```mermaid
flowchart TB
    http["REST browser / MCP HTTP request<br/>External caller"]
    subgraph worker["API worker - container boundary"]
        middleware["ExceptionHandlingMiddleware / TenantAuthorizationMiddleware<br/>Component: invocation boundary and admission"]
        authorization["GalleryRequestAuthorization + profile policies<br/>Component: public allowlist or private bearer admission"]
        identity["EntraTokenValidator / EntraMetadata<br/>Component: cryptographic validation and key refresh"]
        session["GetSession / Catalog / OAuthResourceMetadata<br/>Component group: readiness, registry and discovery"]
        transport["Mcp / McpJsonRpc / McpToolCatalog / PublicMcpGuard<br/>Component: protocol, discovery and bounded admission"]
        adapter["GalleryMcpTools / GalleryRequestFactory / GalleryLinkBuilder<br/>Component: argument adaptation and result projection"]
        handlers["Tasks / Workflows / Methods / Reports / Engagement<br/>Component group: shared catalog handlers"]
        scope["CollectionScope / CollectionAuthorizationPolicy<br/>Component: selected capability and operation check"]
        actor["CurrentUserService<br/>Component: validated user / sandbox attribution"]
        validation["CatalogRegistry / MethodAssociation / FileValidationService<br/>Component group: field, reference and file contracts"]
        ai["AiComposer / PublicComposer / IAiComposerService<br/>Component: proposal-only inference"]
        budget["PublicComposerBudget / AiComposerRateLimiter<br/>Component group: durable public spend / private rate checks"]
        files["IBlobStorageService / PlaygroundUploadBudget<br/>Component: file routing and cumulative upload budget"]
        db["AppDbContext / CatalogSchema<br/>Component: query scope, write stamping and schema verification"]
        feedback["Feedback / IFeedbackNotificationClient<br/>Component: optional validated email delivery"]
        audit["McpAuditLogger / ILogger<br/>Component: outcomes and invocation diagnostics"]
    end
    sql[("SQL<br/>Container")]
    blob[("Blob storage<br/>Container")]
    entra["Entra<br/>External system"]
    model["Foundry / Azure OpenAI<br/>External system"]
    mail["ACS Email<br/>External system"]
    telemetry["Application Insights<br/>External service"]
    http -->|"HTTP invocation"| middleware
    middleware -->|"Authorizes before handler"| authorization
    authorization -->|"Private profile only"| identity
    identity -->|"Signing metadata"| entra
    middleware -->|"Dispatches admitted request"| handlers
    middleware -->|"Dispatches protocol request"| transport
    middleware -->|"Dispatches readiness / metadata"| session
    middleware -->|"Dispatches proposal"| ai
    middleware -->|"Dispatches feedback"| feedback
    transport -->|"Validated tools/call"| adapter
    adapter -->|"Calls same Function methods"| handlers
    handlers -->|"Rechecks scope / operation"| scope
    handlers -->|"Gets attribution"| actor
    handlers -->|"Validates input / associations"| validation
    handlers -->|"Queries / saves"| db
    handlers -->|"Streams / stages / deletes"| files
    session -->|"Reads readiness / catalog state"| db
    ai -->|"Selects catalog context"| db
    ai -->|"Reserves / completes request"| budget
    budget -->|"Durable ledger / request logs"| db
    ai -->|"Bounded model call"| model
    files -->|"Blob operations / budget blob"| blob
    db -->|"EF Core SQL operations"| sql
    transport -->|"Records outcome"| audit
    middleware -->|"Logs invocation failure"| audit
    audit -->|"Worker telemetry pipeline"| telemetry
    feedback -.->|"Only when configured / enabled"| mail
```

### Responsibility map

| Component | Invariant worth teaching |
|---|---|
| Admission middleware | Direct and proxied HTTP calls use the same application gate; HTTP trigger `Anonymous` does not mean unrestricted private API |
| Request authorization | Never turns failed private authentication into public access; metadata provider failure is not a successful sign-in |
| CollectionScope | Resolves and reapplies capability to the selected operation; public published queries do not expose unpublished rows |
| Catalog handlers | REST and MCP reach the same field checks, references, ownership/admin rules and persistence |
| MCP adapter | Rejects invalid arguments and public transport/tool collection mismatch; does not trust caller-created user claims |
| AppDbContext | Filters selected collection and publication; stamps added collection-bearing rows and refuses reassignment |
| File service | Routes by capability, validates prefixes, refuses isolated curated writes and stages unique attachment paths |
| Composer | Uses allowed catalog context and returns a proposal; never creates a workflow as part of inference |

## Import CLI

```mermaid
flowchart LR
    operator["Operator + reviewed JSON<br/>External input"]
    subgraph cli["Catalog import CLI - container boundary"]
        entry["Program<br/>Component: explicit arguments / target / actor"]
        importer["CatalogImportService<br/>Component: bounded envelope and reference validation"]
        contracts["CatalogRegistry + Function validators<br/>Component: shared field and step contracts"]
        transaction["AppDbContext + import receipt<br/>Component: serializable transaction / content and state hashes"]
    end
    sql[("Explicit SQL destination<br/>Container")]
    operator -->|"Invokes with reviewed package"| entry
    entry -->|"ImportAsync"| importer
    importer -->|"Validates same catalog contracts"| contracts
    importer -->|"Empty collection / actor / receipt checks"| transaction
    transaction -->|"Commit, dry-run rollback or refusal"| sql
```

The importer is out-of-band, not a hidden hosted publisher. Its verified scope is
methods/tasks/workflows JSON; attachment bytes need a separately authorized
publication process. A dry run rolls back. A populated or edited destination is
not silently reset to make an import succeed.

**Evidence:** [frontend source](../../../frontend/src/App.tsx),
[AiComposerPanel](../../../frontend/src/components/AiComposerPanel.tsx),
[AuthProvider](../../../frontend/src/auth/AuthProvider.tsx),
[worker composition](../../../api/src/Program.cs),
[Function handlers](../../../api/src/Functions),
[services](../../../api/src/Services),
[MCP adapter](../../../api/src/Services/Mcp/GalleryMcpTools.cs),
[CLI service](../../../api/src/Services/CatalogImportService.cs).
