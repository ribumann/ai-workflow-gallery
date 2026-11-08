# Isolated public demo deployment

This is a **separate, opt-in, fresh installation**, not a switch for an existing gallery.
`infra/public-demo.bicep` and `scripts/deploy-public-demo.ps1` do not migrate existing data.
Publishing a repository or its history is a separate review, not an infrastructure action.

## Boundary and source prerequisites

Deploy only a reviewed source revision that includes the public catalog core and the supported
.NET 10 isolated Functions runtime. The template is not compatible with the earlier private-only
API: that version requires Entra and automatically runs migrations. No dummy Entra app is needed.

The public profile sets `PublicDemoEnabled=true`, `SqlConnectionString`, `StorageBlobEndpoint`,
`StorageContainerName=artifact-files`, `GalleryBaseUrl` and the dedicated runtime `AZURE_CLIENT_ID`.
Build the matching frontend with `VITE_PUBLIC_DEMO_ENABLED=true`; `/api/session` must confirm
`publicDemo:true`. Catalog requests must select exactly one of `collectionId=public-techniques`,
`collectionId=curated-tools` or `collectionId=playground`. These are three scoped collections
within one installation, **not** customer tenancy or separate cloud tenants. The two published
collections expose only published rows and remain read-only. Curated tools starts empty and
receives only independently reviewed, anonymized content; never copy private SQL/blob data or
private repository history. Playground content is shared, public and disposable; anonymous
authoring is unavailable until its explicit write switch and separately reviewed data-plane
protections are installed.

### Manual publication

This release has no hosted publisher, publisher endpoint, or publisher sign-in.
The owner supplies an export/import package after reviewing its content and attachments
for publication rights and private data. Use the existing dataset import tooling for
validated catalog records, against an explicitly chosen destination and operator identity.
Stage attachments separately, preserve their package references, and verify imported
records and downloads before declaring an import complete. Do not import private
repository history, directory identities, or unreviewed files.

The installed Playground SQL guard rejects non-Playground writes by the public runtime.
The public worker retains only a dedicated reader on the separate curated files account;
host storage ownership does not extend to that account. Never grant the anonymous runtime
broader writes or bypass triggers to import a package. Optional private collection membership
is described in [architecture](architecture.md#optional-private-collection-membership);
it must not be enabled together with this anonymous public profile.

Dev/sample/bonus seeds, MCP, feedback mail and the Workflow composer are disabled by default.
MCP has an independent read-only opt-in below; only the separately scoped Playground MCP
connection can expose existing write tools after the SQL/storage review and write opt-in.
No ACS resource, sender, mail role or anonymous feedback/send endpoint is configured.
Optional private model capacity does **not** enable anonymous inference. The guarded proposal
path requires a separate explicit opt-in and all reviewed bounded settings below. Disabled is
the default, not a model failure. Never remove the guard to make a demonstration appear complete.

## Resources and access

General resources are in Sweden Central; only SWA Standard is in East US 2. A new VNet contains
the Flex Consumption Function integration subnet and private SQL/blob/model endpoints with private
DNS. SQL is Entra-only. Blob anonymous access, shared keys, public data-service networking and
trusted-service bypasses are disabled. The API and frontend are intentionally public HTTPS entry
points; public-mode API middleware, not SWA cookies/headers, enforces the published collection boundary.

The template creates a dedicated user-assigned runtime identity and a separate Playground
storage identity. The runtime identity's ARM grants are Storage Blob Data Owner on the new
host storage account (Functions host/deployment package requirement), and, if explicitly provisioned,
OpenAI User on the exact new AI account. The Playground identity receives Blob Data Reader on
the dedicated `playground-files` container in a **different storage account**, plus Blob Data Contributor there only under the
explicit Playground-write opt-in. The API selects this identity only for Playground attachments.
No subscription grant, mail grant, existing identity reuse or SQL-admin assignment to the runtime
is performed.

The separate account has its own private blob endpoint and no host-runtime role.
Curated files likewise use the separate `stcur<token>` account and `curated-files`
container, with `CuratedStorageBlobEndpoint` / `CuratedStorageClientId` selecting the
public reader. Its private endpoint is created after the Playground endpoint to
serialize updates to the shared private DNS zone/link. Freshness/exclusion checks
include both attachment accounts and the curated-reader identity.
`PlaygroundStorageBlobEndpoint` routes the dedicated identity there; startup refuses
public writes if that endpoint is absent or equal to `StorageBlobEndpoint`. Keep blob
versioning, snapshots and soft-delete retention disabled so old bytes are not retained
outside the upload budget.

SQL administration is a **separately approved** same-tenant bootstrap group/application, supplied
explicitly. An authorized administrator with private VNet connectivity must review/apply the
fresh schema, then create the dedicated runtime's contained SQL user and grant the exact necessary
database read permissions (plus only the explicit ledger permissions for opted-in Composer below).
The runtime is never the SQL server administrator. Bootstrap requires no public SQL exception,
directory membership change, Directory Readers grant, shared credentials or blanket `db_owner`.
The helper does not execute SQL or initialize content.

## Preview and exact approval

Prerequisites: PowerShell 7.2+, Bicep CLI, Azure CLI, and an existing **empty** dedicated RG in Sweden
Central. The operator must independently approve the subscription/tenant/RG, authenticate an isolated
CLI profile, review provider registration, region/policy availability, SQL-admin identity, RBAC
creation authority and costs. The helper does not create the RG, register providers, login/logout,
change the active global subscription or modify Entra. Never point it at the default `.azure` profile.
No target is preapproved by this document.

Create a local (untracked) reviewed exclusions JSON with these nonempty arrays:
`resourceGroupIds` (full ARM IDs of protected existing resource groups), `resourceNames`
(protected resource/database names), and `identityIds` (protected runtime principal and client
GUIDs). Include every existing environment that must remain untouched. Do not use blanket
subscription/tenant exclusions: an explicitly approved shared subscription/tenant is supported
when the RG, resources, SQL database and runtime identity are fresh and isolated. The helper
refuses a target listed in the reviewed exclusions file; the operator must verify that the
file includes every protected environment and must not rely on an implicit target exclusion.
Names of new resources are derived exclusively from a new lowercase alphanumeric `NameToken`:
`func-public-<token>`, `sql-public-<token>`, `catalog-public-<token>`, `stpub<token>`,
`id-public-<token>-runtime`, `swa-public-<token>` and `vnet-public-<token>`.
Attachment accounts are `stpg<token>` and `stcur<token>`; their dedicated identities
are `id-public-<token>-playground-storage` and `id-public-<token>-curated-reader`.
No existing resource or database may be attached. Choose a token not used by any other gallery.

Use explicit operator-selected values; the following is a command shape, not approved defaults:

```powershell
$target = @{
    SubscriptionId = '<approved-public-subscription-guid>'
    TenantId = '<approved-public-tenant-guid>'
    ResourceGroup = '<approved-empty-public-rg>'
    NameToken = '<new-6-to-14-character-token>'
    SqlAdminObjectId = '<approved-bootstrap-group-object-guid>'
    SqlAdminLogin = '<approved-bootstrap-group-display-name>'
    SqlAdminPrincipalType = 'Group'
    AzureConfigDirectory = '<absolute-isolated-authenticated-cli-profile-directory>'
    ExcludedTargetsPath = '<absolute-local-private-exclusions.json>'
    ReviewPath = '<absolute-new-review-bundle.json>'
}
# Offline: real compilation and local target guards only. No Azure calls, no approval bundle.
.\scripts\deploy-public-demo.ps1 @target -Offline
# Default online action: account/RG/freshness/name checks and ARM what-if; no resource writes.
.\scripts\deploy-public-demo.ps1 @target
```

Inspect the saved bundle and exact SHA256 printed by preview. It binds the explicit target,
compiled template (including reusable modules), parameters, exclusions and complete what-if
change set. Review **all** nested changes and account-scoped role assignments, recurring SQL/
SWA/private-endpoint/monitoring/storage costs and any optional model capacity. Unknown, failed,
non-create or out-of-scope what-if results stop the helper.

Only after explicit approval:

```powershell
.\scripts\deploy-public-demo.ps1 @target -Apply -ApproveCosts -ApproveRoleAssignments `
    -ReviewedSha256 '<exact-approved-64-character-preview-bundle-sha256>'
```

Apply rechecks account, tenant, empty RG, name availability, compilation, exclusions and a fresh
identical what-if. Changed inputs require a new preview at a new path and approval. It creates
infrastructure exactly once and never retries a failed/partial installation. Any nonempty RG
is rejected, including a previous installation: this helper deliberately does not implement
in-place updates, teardown, resource adoption, NSG edits or rollback.
ARM is not transactional; an external change between final checks and creation can still race.
Use exclusive change ownership of the fresh RG during review/apply.

Optional capacity is another explicit preview/approval, adding these arguments **to both calls**:
`-ProvisionAiModel -AiModelName gpt-5.4-mini -AiModelVersion 2026-03-17 -AiModelSku DataZoneStandard
-AiModelCapacity 10`. Verify current Sweden Central model capability, EU data-zone residency,
quota and costs immediately before approval. Template compilation is not a capability/quota or
cost check. Provisioning it incurs capacity/call-related costs even though this profile keeps the
application Composer disabled; omit it until the reviewed application guard path is ready.

### Explicit Composer opt-in

Only the bounded, read-only proposal path may be enabled. It never grants anonymous
catalog writes, upload/import, feedback, engagement, admin, MCP writes or private integrations.
The helper requires **every** numeric parameter to be explicitly supplied when
`-EnablePublicComposer` is present, as well as `-ProvisionAiModel` and explicit
model name/version. Model provisioning alone keeps both inference flags false.
The following conservative cap example must be added identically to preview and
approved apply (not an authorization to provision or enable anything):

```powershell
$composer = @{
    ProvisionAiModel = $true
    AiModelName = 'gpt-5.4-mini'
    AiModelVersion = '2026-03-17'
    AiModelSku = 'DataZoneStandard'
    AiModelCapacity = 10
    EnablePublicComposer = $true
    PublicComposerDailyRequestLimit = 10
    PublicComposerDailyTokenLimit = 100000
    PublicComposerMaxConcurrentRequests = 1
    PublicComposerMaxGoalCharacters = 1000
    PublicComposerMaxContextBytes = 12000
    PublicComposerMaxOutputTokens = 1024
    PublicComposerModelTimeoutSeconds = 20
}
.\scripts\deploy-public-demo.ps1 @target @composer -Offline
# Online preview and approved apply use the same @composer and new review bundle.
```

The compiled public profile sets `PublicDemoEnabled=true`; only this reviewed
opt-in sets `EnableAiComposer=true` and `PublicComposerEnabled=true`. It wires the
new private model's `AiFoundryEndpoint` / `AiFoundryDeploymentName`, not an
operator-supplied existing endpoint, and emits all seven `PublicComposer__...`
settings. Hard app maxima are 100 requests/day,
2500000 tokens/day, concurrency 2, goal 1000 characters, context 16000 UTF-8 bytes,
2048 output tokens, and a 30-second model deadline. The provisioning template and deployment helper accept the reviewed 2500000 tokens/day
ceiling, while keeping the conservative 100000 tokens/day default; explicit opt-in and
the other reviewed bounds remain required.
All limits must be positive.
Daily tokens must cover at least one worst-case reservation: context cap +
4 x goal cap + 4096 system-prompt byte envelope + 1024 framing + output cap
(22144 tokens with the example). The app checks its system prompt fits the envelope.
Exact limits and flags are bound into the existing reviewed parameter hash.

Public Composer sends the configured output cap as `max_completion_tokens`, not
legacy `max_tokens`, for the reasoning deployment. It retains JSON-object response
format and sends no sampling parameters. A recording HTTP transport reproduced
the original Azure.AI.OpenAI 2.1.0 / OpenAI 2.1.0 serialization of `max_tokens=1024`.
The API now uses Azure.AI.OpenAI **2.9.0-beta.1** with its compatible OpenAI **2.9.1**
dependency and the SDK's explicit modern-token serialization opt-in. The preview
Azure package is deliberate: its changelog restores OpenAI compatibility and fixes
the token opt-in implementation; no custom HTTP rewriting or reflection workaround
is used. The existing private path retains its legacy output-token field and 4096
cap. Both paths pin REST API version `2024-10-21`, preserve the configured endpoint
and managed-identity audience, and disable automatic SDK retries.
See the [SDK release history](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/CHANGELOG.md)
and [reasoning model parameter guidance](https://learn.microsoft.com/azure/foundry/openai/how-to/reasoning).

Provider failures log HTTP status plus only allowlisted provider error codes and
parameter names from a bounded error envelope. Unknown/absent identifiers become
`unrecognized`/`unavailable`; messages, bodies, exception objects, goal/catalog/system
prompts and credentials are never logged. This wire-level compatibility evidence
does not prove a live model call will succeed or establish the cause of an earlier
uninspected HTTP 400. Do not retry inference, refund/reset reservations, increase
caps or change networking to diagnose it; live acceptance is a separate budgeted action.

The SQL ledger serializes reservations across **all** processes, instances and
collections, using SQL's UTC clock and an exclusive application lock before
reading totals. It commits before the single model call; no transaction spans
inference. There are no refunds: failure, cancellation, unknown delivery, crashes
and retries conservatively consume the worst-case daily tokens/request. Success
releases only the concurrency lease; an unconfirmed call retains a 300-second
lease before capacity recovers. No SDK inference retries or IP/cookie budget.
UTC daily reset does not clear active leases. Provider-side work after timeout is
unobservable; lease bounds describe admitted calls, not guaranteed provider termination.

`/api/session` keeps `isAuthenticated/isAuthorized=false` and independently
reports SQL/config readiness (`composerState`: disabled, unavailable,
quota_exhausted, busy or ready) and the configured public goal limit.
Readiness does not prove model deployment/identity access; POST rechecks budgets.
Disabled is 403, unavailable is 503, quota/busy is 429 with `Retry-After`, no
relevant published context is 422, and model failure is 502. No fake proposal
or identity is used. See [the full guard contract](catalog-core.md#optional-bounded-anonymous-composer).

### Explicit public MCP opt-in

The matching API revision adds `EnablePublicMcp`; older Composer-only packages do not
implement it. Keep SWA and Functions together and use the exact endpoint selected for
the deployment, `<public-origin>/api/mcp`. Source or browser acceptance does not prove
that Playground writes have been deployed. Publish matching reviewed API/frontend
artifacts before separate write activation.
Published reads require no SQL write grant, Entra registration, model capacity or
Composer ledger permission. Playground writes require the separately reviewed SQL
grants and storage role below.

The public template/helper default both `EnableMcpServer` and `EnablePublicMcp` to
false. Read-only access requires **all three** app settings `PublicDemoEnabled=true`,
`EnableMcpServer=true`, and `EnablePublicMcp=true`. `EnableMcpWrites=false` remains
fixed in this template. The two published collections always expose the eight existing
read tools. The explicitly selected Playground connection exposes those same reads and
twelve existing write tools only when `PublicPlaygroundWritesEnabled=true` and the
database/storage protections below are active. Private Entra deployments retain their
existing master/write switches and validated delegated identity; there is no fallback
to anonymous after failed auth.

For a **fresh** installation only, include the same explicit arguments in preview
and approved apply:

```powershell
.\scripts\deploy-public-demo.ps1 @target -Offline -EnableMcpServer -EnablePublicMcp `
    -PublicMcpAllowedOrigins 'https://gallery.example.org'
# Online preview and approved apply use identical flags, origins and a new review bundle.
```

`PublicMcpAllowedOrigins` is a comma-separated app setting of exact approved HTTPS
origins (no path, credentials or trailing slash). The template also approves its own
generated SWA origin. Nonbrowser clients may omit `Origin`; supplied unapproved,
`null`, malformed or multiple Origins receive 403. This is browser-origin checking,
not an identity or authorization substitute. See the [connection guide](connect-production-mcp-server.md)
for explicit anonymous and private modes, including collection selection.

Only exact `/api/mcp` GET/POST/DELETE transport requests are admitted. Public MCP
requires an explicit `collectionId` on the transport request and the same value on
every tool call; no endpoint or connection fallback is used. POST supports
initialize, notifications, ping, tools/list and tools/call; GET returns 405 (no SSE),
DELETE 204 (no stored sessions). Discovery and execution share the same eight-read-tool
allowlist for published collections; Playground discovery adds the twelve existing
catalog write tools only under its separate collection capability. Reads and writes use
the existing scoped HTTP handlers and hidden-navigation redaction. MCP writes cannot
change public/curated rows, grant feedback/admin access, infer, reserve Composer budget,
fetch external data or execute tools described by catalog documents.

Fixed public limits are **64 KiB actual streamed request**, **25 items per search page**
(larger pages are rejected), **512 KiB complete serialized UTF-8 response** (including
both text and structured representations), and a **20-second deadline**. Oversized
request/response returns 413, timeout 504, and admission rejection 429 with `Retry-After`.
JSON is never truncated to fit. Client disconnects cancel delegated work. An operation
that ignores cancellation retains its concurrency slot until it actually stops.
The rolling limit is **60 admitted POSTs per minute and two active POSTs per worker**.
Scale-out multiplies those limits; these are not global quotas, a spending guarantee,
or a complete denial-of-service defense. They do not reuse the paid Composer ledger.
Request bodies, tokens and prompts are not logged.

Disable `EnablePublicMcp` to roll back public MCP without changing browser reads or
Composer. No cloud configuration, DNS, publishing or activation is performed by tests.
The fresh-only helper must **not** be rerun against a partially created/nonempty RG.
The public Function's Blob endpoint now comes from the completed storage module output,
avoiding the earlier eager existing-storage read; recovery of an existing partial
deployment still requires a separately reviewed, narrow operation.

### Playground write activation and data-plane review

The public catalog branch does not enable writes merely because the Playground appears
in the collection picker. Keep `PublicPlaygroundWritesEnabled=false` until the exact
database and storage changes have been reviewed and applied. The available additive
SQL artifacts are:

- [`apply-playground-policy.sql`](../deployment/playground/apply-playground-policy.sql):
  one-time policy install on a fresh `white-label-catalog-v4-playground-writes` schema,
  or migration from the reviewed `white-label-catalog-v2-public-composer` schema to v4;
  null-identity sandbox actor seed,
  Playground-only triggers, cross-collection reference guards, row/file-byte bounds,
  and object-level grants to the pre-existing runtime SQL user. It fails unless exact
  database/user placeholders are replaced and the runtime is neither `db_owner` nor
  `db_datawriter`. It is not rerunnable.
- [`disable-playground-policy.sql`](../deployment/playground/disable-playground-policy.sql):
  non-destructive rollback after the application flag and storage writer role are off.
  It removes only the Playground triggers and direct DML grants; additive schema, actor,
  and data remain.
- [`reset-playground.sql`](../deployment/playground/reset-playground.sql):
  a separately confirmed, destructive catalog reset constrained to
  `CollectionId='playground'`. Its serializable preflight rejects cross-collection references
  before the first delete, preventing `WorkflowSteps` cascade or `SET NULL` effects on
  non-Playground rows. It leaves public/curated rows, the sandbox actor and Composer ledger
  untouched when the preflight succeeds.

Apply the SQL only through the reviewed private SQL path as the database administrator.
Never use application startup, the public-demo fresh-install helper, `db_owner`, or a
blanket catalog writer role to perform this change. The row cap is 5,000 catalog/engagement
rows and the saved attachment cap is 512 MiB. The API also bounds write bodies to 256 KiB
for catalog JSON and 6 MiB for task multipart requests, with at most 60 admissions/minute
and four active writes per worker; scale-out multiplies those in-process request limits.
The SQL bounds serialize concurrent writes across workers.

The Playground uses the dedicated `playground-files` container in a separate account and a separate managed
identity. The API routes file operations to that container only for Playground scope.
Review the exact container-level Blob Data Contributor role before adding it; the default
deployment has only the reader role and keeps the write flag off. A Playground reset must
clear that dedicated container separately after reviewing the exact account/container.
Do not delete the main `artifact-files` container or reset any public/curated assets.

An ETag-protected `playground/.upload-budget` blob reserves a cumulative 512 MiB of
attachment uploads across workers before upload. All failed/cancelled/uncertain operations
retain their reservation, as do replacements and deletes; this is deliberately conservative
and independent of the SQL saved-row byte cap and Composer ledger. Budget exhaustion or
contention returns 429. Never reset this counter alone. Disable writes, drain all in-flight
requests, run the reviewed SQL reset, then clear the **entire** dedicated container before
re-enabling writes. Existing installations must provision a fresh separate account/container,
not move the counter or enable writes against host storage.

Safe activation sequence for a separately authorized operator:

1. Verify the exact deployed API revision, database, schema marker, contained runtime user,
   effective database roles, storage account/container and identity assignment.
2. Review and apply the one-time SQL artifact; verify its schema marker and trigger/grant
   inventory using a read-only query.
3. Add only the dedicated Playground container writer role, then verify the effective role
   scope. Do not grant the general runtime identity broader catalog or storage access.
4. Preview the application-setting change with `PublicPlaygroundWritesEnabled=true`; apply
   only after the backend protections are present. Keep `EnableMcpWrites=false`.
5. Test positive Playground CRUD/files and negative public/curated/cross-collection access
   with disposable records. Remove only those records, and verify published asset hashes.

This repository task prepares review artifacts only. It does not execute this sequence,
change cloud roles/settings, reset data, or claim live Playground write acceptance.

## Schema, content and code release gates

After infrastructure creation, use a separately authorized private-network runner and the
approved bootstrap identity against **only** the exact new server/database returned by deployment.
The public core's offline schema printer (after the supported-runtime/public-core changes):

```powershell
dotnet build api\src\src.csproj --configuration Release
if ($LASTEXITCODE -eq 0) {
    dotnet api\src\bin\Release\net10.0\src.dll --print-catalog-schema
}
```

Invoke the built DLL directly: the Functions Worker SDK overrides `dotnet run`, even with
`--no-launch-profile`, and otherwise attempts to start the Functions host instead of the printer.
It emits CREATE statements and the `CatalogSchemaMarker` row
`(Id=1, Version='white-label-catalog-v4-playground-writes')` without connecting to a database. The printer itself
does **not** check/guard a remote apply target. An authorized private-network operator must
independently verify and apply the reviewed SQL only to the exact new empty database.
Normal startup only SELECTs/verifies the marker; it does not migrate/create the schema.
Do not invoke a legacy EF migration, dev seed or `--initialize-catalog` against cloud SQL.
The explicit local initializer is not a remote provisioning shortcut.

After schema creation, provision the dedicated contained runtime user. When creating a user
with `WITH SID ... TYPE=E` for a service principal or
managed identity, use the verified **`runtimeClientId` (application/client ID), not
`runtimePrincipalId` (object ID)**. ARM role assignments still use `runtimePrincipalId`; these
identifiers have different purposes. For example, after verifying the exact new database:

```sql
DECLARE @RuntimeClientId uniqueidentifier = '<verified-runtime-client-id>';
DECLARE @RuntimeSid binary(16) = CONVERT(binary(16), @RuntimeClientId);
DECLARE @CreateUser nvarchar(max) =
    N'CREATE USER [runtime-managed-identity-sql-user] WITH SID=' +
    CONVERT(nvarchar(34), @RuntimeSid, 1) + N', TYPE=E;';
EXEC sys.sp_executesql @CreateUser;
```

See Microsoft's [contained-user SID examples](https://learn.microsoft.com/sql/t-sql/statements/create-user-transact-sql#examples).
Verify an actual runtime-identity token login and `CatalogSchema.VerifyAsync` over private
networking before deploying the host. `EXECUTE AS USER` checks grants but does not validate
the Entra token-to-user mapping and cannot replace that check.

The contained runtime user needs SELECT on the marker and related catalog
tables (tasks/workflows/steps/methods/reports and user/file/engagement metadata). The dedicated
database's `db_datareader` role is a simple read-only grant; no DDL, `db_datawriter`, admin or
`db_owner` is needed for read-only mode. Playground writes require the separately reviewed
object-level grants and triggers above. Use a separate bootstrap/import principal.
For explicitly opted-in Composer only, the same contained runtime user additionally needs:

```sql
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.PublicComposerReservations
    TO [runtime-managed-identity-sql-user];
```

Replace the placeholder with the existing dedicated runtime SQL user. No DELETE,
extra EXECUTE grant, DDL, admin or broad writer role is needed. The short
transaction-owned `sys.sp_getapplock` uses SQL's public database principal permission,
as in private Composer's existing reservation mechanism. Retain all ledger rows
for accounting; deletion is not quota recovery. The v4 marker and startup SELECT
verify every ledger column. Initialize v4 directly in a new empty database, never
an older schema followed by an ad hoc migration/reset. Normal startup does not create tables,
users or grants. An operator must verify the exact ledger grants over the private
SQL path before enabling inference; a missing/unavailable store fails closed.

Use the reviewed public corpus importer with its explicit target database and a real actor,
first dry-run, then separately approved import/publication. Publishing reviewed public-source
records and anonymized curated records is distinct from inserting drafts. Import must never
discover or fall back to another connection/catalog. Unpublished records must stay invisible.

Publish the .NET 10 API with the repository's supported SDK:
`dotnet publish api\src\src.csproj --configuration Release --output api-publish`.
Package the **contents**, not the enclosing directory; the ZIP root must contain `host.json`,
`worker.config.json`, `functions.metadata`, the runtime config and `.azurefunctions`.
Verify ZIP entry permissions as well as paths and content hashes: Linux-hosted Functions must
be able to read `host.json` and every assembly. Use POSIX regular-file mode `0644` and
directory mode `0755`, not owner-only `0600` (the default for Python `ZipFile.writestr`).
A successful upload does not prove host startup; retain the deployment log and require
healthy HTTP responses plus the expected indexed function inventory.
Use an independently approved Entra-authenticated Flex OneDeploy release against the exact
returned public Function resource; its managed identity writes package storage through its VNet.
No basic publishing credentials, storage key or public blob/SQL exceptions.
This helper intentionally does not publish binaries/frontend, set up deployment credentials,
fetch publishing tokens or alter CI environments. SWA deployment authentication and the release
identity's narrowly scoped access must be reviewed separately; do not reuse credentials or
secrets from another environment.

Before announcing a live demo, verify the deployed `/api/session` public contract, both explicit
collection views and their published-only filtering; direct Function requests as well as SWA-proxied
requests; rejected writes/feedback/private reads; opted-in MCP initialize, eight-tool discovery,
one scoped read in each collection and rejected forged writes (or denied MCP when disabled);
private data-service networking; disabled/
exhausted Composer responses; branding, public-source provenance and anonymization. A source build
or mocked browser test does not establish a deployed anonymous/authentication boundary.

Offline regression suite: `pwsh -File scripts\test-deploy-public-demo.ps1`.
It compiles actual Bicep and uses strict CLI doubles for preview/apply rejection and approval paths;
it never connects to Azure and does not establish live deployment readiness.
