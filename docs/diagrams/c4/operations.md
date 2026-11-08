# Supporting view: operations, decisions and trade-offs

[Architecture tour](../../architecture.md) | [Back to context](context.md)

**Learn:** a compelling architecture explains not just the boxes, but why they
are shaped that way and what can go wrong. The original repository's
cross-feature architecture used that approach; these decisions are restated
against current code rather than copying its historical authentication,
.NET version, feedback or infrastructure assumptions.

These are explanations of implemented choices, not newly approved architectural
changes or a retroactively invented ADR history.

## Design decisions and their costs

| Decision | Why it fits this application | Consequence / alternative not implemented |
|---|---|---|
| One modular API worker | Shared handlers and a modest catalog keep authorization, validation and behavior in one place | Coupled deploy/failure domain; not independent per-feature microservices |
| Separate SPA and standalone Function App | Static hosting plus a linked backend permits outbound VNet integration for private data dependencies | Two deployment artifacts; frontend/API contracts and schema profiles must stay compatible |
| Worker-enforced bearer validation in private profile | Works on direct and SWA-proxied requests, with actual delegated issuer/audience/scope/identity checks | Requires owned registration/settings and metadata availability; SWA headers/cookies are not the security boundary |
| Explicit anonymous public profile | Published discovery without pretending visitors have directory identities | Cannot turn a failed private sign-in into anonymous access; public write exceptions must be narrow and opt-in |
| Request-scoped collection capability | Same selection informs UI, REST, MCP and EF query/write rules | Application partition, not customer tenancy or standalone database isolation |
| MCP delegates to REST Function methods | Prevents drifting search, validation, ownership and concurrency implementations | Adapter remains tied to handler request/response contracts |
| Stateless JSON MCP | Short queries work with Functions scale-out and no server session affinity | No SSE subscriptions or server-initiated work; not evidence of every native client's compatibility |
| Live catalog context, no vector/search service | Composer and discovery use the authoritative catalog rather than another stale index | Some filters/candidate selection materialize rows; scale limits are not solved by a diagram |
| Proposal-only Composer | AI suggestions cannot silently mutate or execute catalog content | Human review and a separately authorized save remain necessary |
| Durable public Composer spend ledger | Global requests/tokens/concurrent leases survive worker scale-out | Shared SQL lock/store becomes a dependency; conservative charges on uncertain calls |
| Separate curated-reader and Playground file credentials in public source profile | Anonymous authoring cannot reuse curated file writer rights | More accounts/identities/DNS and deployment review; ordinary private editing remains a different contract |
| Unique staged Blob uploads and compensation | A rejected metadata update must not overwrite the saved attachment | No distributed SQL/Blob transaction; crash windows and orphan reconciliation remain |
| Schema verification, never startup migration | Prevents accidentally converting a legacy/private database during startup | Schema preparation is explicit operator work; wrong worker/schema fails rather than self-heals |
| Transactional empty-collection importer | Reviewed packages reuse field/reference checks and produce repeatable receipts | Not a general merge/restore tool or hosted publisher; rejects populated/edited destinations |
| Runtime feature opt-ins | Model, MCP, public writes and feedback can be unavailable without implying broad authorization | Provisioned resource != working feature; UI/server/data-plane flags must match |

## Failure and recovery model

| Failure | Observable behavior | Safe recovery boundary |
|---|---|---|
| Missing/expired private token | 401; SPA requests reauthentication and keeps mounted drafts | Person completes provider flow, session is rechecked, failed operation is retried manually |
| Tenant/scope/collection denial | 403; no anonymous fallback | Correct authorized configuration/membership, not token/header fabrication |
| Entra metadata or auth configuration unavailable | 503 and logged provider/config failure | Restore provider/config health; do not report successful sign-in |
| Wrong/uninitialized schema | Worker startup fails verification | Use matching worker/schema; empty local initialization or separately reviewed schema operation, never reset private DB |
| Missing item/publication | REST 404 or MCP tool error | Correct selected collection/reference; do not broaden search to private data |
| Model timeout/failure/invalid proposal | Explicit unavailable/error; no catalog save | Manual catalog use remains; public reservation stays charged |
| Public Composer quota/concurrency | 429 with Retry-After; no authorized inference | Wait within same target; do not automatically retry uncertain spend |
| Public MCP/body/response/deadline limits | Explicit 413, 429 or 504 as applicable | Bounded requests, obey Retry-After; no hidden truncation or target switching |
| Attachment save/cleanup failure | Explicit failure; compensation where supported | Reconcile the exact staged/saved paths; never broad account/container deletion |
| SQL auto-pause/cold start | First request/startup can pay resume/initialization latency | Deliberate cost/latency settings; timeout is not data expiry or a stopped database's deletion |
| Feedback unconfigured/disabled | Handler does not establish successful email delivery | Delivery is deferred here; independently approve/configure the provider path if later required |

## Scale and budgets: know which scope you are measuring

| Mechanism | Scope | Limitation |
|---|---|---|
| Public MCP admissions | Per worker: 60/minute, 2 active, 20 s, 64 KiB request, 512 KiB response, page size <=25 | Scale-out multiplies aggregate admissions; not a global spend ledger |
| Playground REST admission | Per worker: 60/minute, 4 active; bounded bodies | Not a distributed rate limiter or complete denial-of-service protection |
| Playground upload counter | Durable dedicated Blob: 512 MiB cumulative reserved bytes | No refunds; not total account billing or a retention guarantee |
| Playground catalog policy | Reviewed SQL policy includes bounded rows and cross-collection guards | SQL artifacts must actually be applied; source EF trigger declarations do not install/prove guards |
| Public Composer ledger | Shared database-wide daily request/token reservations and active leases | Conservatively charged reservations are not exact provider billing |
| Private Composer limiter | User request logs in SQL | Separate mechanism, not the anonymous ledger |
| Functions / serverless SQL | Environment-specific capacity, warm instances and SQL pause settings | Template maxima are not tested throughput, autoscaling guarantees or an SLA |

## Observability and maintenance

Exception middleware logs unhandled invocation failures. MCP records method/tool
outcomes and elapsed time through its audit logger. Public logs minimize caller
and argument exposure; private identity-derived logs are not a public artifact.
Application Insights and its Log Analytics workspace are wired from settings;
the workspace source retention default is 30 days. This is monitoring, not a
complete business-event ledger or independently certified audit trail.

No periodic purge, automated Blob reconciliation, backup recovery workflow,
active-active region failover or full incident runbook is established by this
source. Azure-managed backup capabilities do not prove a tested restore. Shared
disposable Playground means an operator-controlled disposable environment, not
an automatic TTL promise. Runtime and schema/data grants must be checked before
enabling optional writes.

Code, SPA assets, schema family, auth audience/scope, canonical MCP resource
origin and collection capabilities form a compatibility set. Deploying a public
worker into a legacy private installation can break the whole API even when its
frontend looks unchanged. Keep matching profiles and preserve previous recovery
artifacts rather than assuming a generic latest package is safe.

## Evidence map: tests vs live acceptance

| Boundary | Source evidence | What it does not establish |
|---|---|---|
| Bearer and collection admission | [TenantAuthorizationAdapterTests](../../../api/tests/TenantAuthorizationAdapterTests.cs), [CollectionAuthorizationTests](../../../api/tests/CollectionAuthorizationTests.cs) | Live cross-user membership or provider expiry recovery |
| REST/MCP shared behavior | [McpParityTests](../../../api/tests/McpParityTests.cs), [McpCollectionScopeTests](../../../api/tests/McpCollectionScopeTests.cs) | Universal native-client compatibility |
| Public authoring guards | [PublicPlaygroundRestTests](../../../api/tests/PublicPlaygroundRestTests.cs), [PublicPlaygroundMcpTests](../../../api/tests/PublicPlaygroundMcpTests.cs) | Deployed SQL grants/triggers or public-domain write activation |
| File isolation / cumulative budget | [CuratedStorageTests](../../../api/tests/CuratedStorageTests.cs), [PlaygroundUploadBudgetTests](../../../api/tests/PlaygroundUploadBudgetTests.cs) | Independent live Blob role inventory or full account enumeration |
| Proposal and spend | [PublicComposerTests](../../../api/tests/PublicComposerTests.cs), [PublicComposerBudgetTests](../../../api/tests/PublicComposerBudgetTests.cs) | Unlimited inference capacity, model quality for every goal or exact billing |
| Catalog persistence | [CatalogStorageTests](../../../api/tests/CatalogStorageTests.cs), [CatalogCoreTests](../../../api/tests/CatalogCoreTests.cs) | A tested restore of the retained private database |
| Release/deployed scope | [Validation index](../../playground-validation.md) | Unperformed acceptance branches or future deployment status |

## Check your understanding

1. **Does a hidden edit button secure a collection?** No. The API rechecks
   operation capability and item rules, then persistence/storage guards apply.
2. **Does private networking identify a person?** No. Network reachability,
   delegated human authorization and worker data-plane identity are separate.
3. **Can an assistant execute the saved workflow?** Not through this catalog.
   It can read the document or request explicitly enabled catalog mutations.
4. **Does a successful proposal create a workflow?** No. Only budget state
   changes during the public inference flow; save is a separate human-reviewed
   authorized request.
5. **Does the public-template Playground account mean live public writes?**
   No. Source topology, installed resources, reviewed permissions and runtime
   opt-in are distinct; public-domain writes remain off at release.
6. **Why is deployment not "Level 5"?** C4's four zoom levels describe logical
   structure. Deployment and dynamic views describe different dimensions of the
   same structure rather than a deeper code zoom.

**Evidence:** [host/DI](../../../api/src/Program.cs),
[MCP guard](../../../api/src/Services/Mcp/PublicMcpGuard.cs),
[Playground admission](../../../api/src/Services/PlaygroundWriteGuard.cs),
[budget blob](../../../api/src/Services/PlaygroundUploadBudget.cs),
[SQL policy](../../../deployment/playground),
[telemetry module](../../../infra/modules/app-insights.bicep),
[schema](../../../api/src/Data/CatalogSchema.cs),
[validation caveats](../../playground-validation.md).
