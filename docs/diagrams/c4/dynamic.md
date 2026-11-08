# Dynamic views: follow a request

[Architecture tour](../../architecture.md) | [Next: deployment](deployment.md)

**Learn:** static C4 arrows say who can collaborate. Dynamic views show the order,
decisions and failure paths for one scenario. These are implementation-derived
sequences, not claims that every branch was exercised on every live deployment.

## 1. Read a task: private and public profiles

```mermaid
sequenceDiagram
    actor Reader
    participant SPA as Browser SPA
    participant Entra as Entra ID
    participant Edge as Static Web Apps
    participant Auth as Worker admission
    participant Handler as Tasks.GetById / CollectionScope
    participant DB as AppDbContext / SQL
    Reader->>SPA: Open collection-scoped task URL
    alt Private profile
        SPA->>Entra: MSAL delegated sign-in / token acquisition
        Entra-->>SPA: API access token through provider flow
        SPA->>Edge: GET /api/tasks/id?collectionId=... + bearer
        Edge->>Auth: Forward linked API request
        Auth->>Entra: Obtain cached/refreshed signing metadata if needed
        Auth->>Auth: Validate token and collection admission
        alt Invalid token / missing scope or grant / provider failure
            Auth-->>SPA: 401 / 403 / 503 - do not execute handler
        else Admitted
            Auth->>Handler: Trusted human principal
            Handler->>Handler: Apply selected collection and read capability
            Handler->>DB: Query scoped task
            DB-->>Handler: Task or no match
            Handler-->>SPA: 200 detail or 404
        end
    else Explicit public profile
        SPA->>Edge: GET /api/tasks/id?collectionId=... - no token
        Edge->>Auth: Forward linked API request
        Auth->>Auth: Reset principal, stamp public policy, allowlist route
        Auth->>Handler: Anonymous admitted request
        Handler->>DB: Query selected collection, published-only for curated catalogs
        DB-->>Handler: Task or no published match
        Handler-->>SPA: 200 detail or 404
    end
    SPA-->>Reader: Render result or explicit error
```

A public collection ID cannot reveal a private installation: it only selects
within the current installation. Private expiry triggers a reauthentication
notice without dropping mounted drafts. Recovery rechecks the session, and the
person retries the operation; the client does not blindly replay a write.
Invalid/missing public collection selection is rejected, not replaced with a
cross-collection search.

## 2. An MCP read reuses the REST handler

```mermaid
sequenceDiagram
    participant Client as MCP client
    participant Auth as Worker admission
    participant MCP as Mcp / PublicMcpGuard
    participant Tools as Tool catalog / GalleryMcpTools
    participant Handler as Tasks.List / CollectionScope
    participant DB as AppDbContext / SQL
    Client->>Auth: POST /api/mcp?collectionId=... - initialize
    Auth->>MCP: Admit by selected profile
    MCP-->>Client: Protocol capabilities, no session ID
    Client->>Auth: POST tools/list
    Auth->>MCP: Reauthorize this HTTP request
    MCP-->>Client: Capability-filtered tool schemas
    Client->>Auth: POST tools/call search_tasks, explicit collectionId
    Auth->>MCP: Reauthorize
    MCP->>MCP: Public origin/body/deadline/admission checks if public
    MCP->>Tools: Find permitted tool and validate arguments
    Tools->>Tools: Public transport/tool collection equality
    Tools->>Handler: Adapt request, preserving trusted context
    Handler->>Handler: Recheck selected collection/read operation
    Handler->>DB: Same paged query as browser REST
    DB-->>Handler: Paged task result
    Handler-->>Tools: IActionResult
    Tools-->>MCP: Structured result + gallery links or tool error
    MCP->>MCP: Audit outcome, public response-size check
    MCP-->>Client: JSON-RPC result / explicit protocol or tool error
```

Each POST stands alone: no SSE, server session or instance affinity.
GET explicitly declines a server event stream; DELETE has no server state to
remove. Disabled tools cannot be enabled by naming them in `tools/call`.
Public read tools number eight; the twelve catalog write tools are additionally
available only for a properly enabled Playground. Private writes have their own
independent MCP write switch plus ordinary authorization.

## 3. Public Composer: charge, infer, validate, propose

```mermaid
sequenceDiagram
    actor Person
    participant SPA as AiComposerPanel
    participant Gate as Worker admission
    participant Composer as PublicComposer
    participant SQL as SQL catalog / budget
    participant Model as AzureAiComposerService / Foundry
    Person->>SPA: Enter goal and request proposal
    SPA->>Gate: POST /api/ai/compose?collectionId=...
    Gate->>Composer: Explicit public proposal opt-in
    Composer->>Composer: Readiness, bounded goal-only JSON
    Composer->>SQL: Select relevant published task snapshots in collection
    SQL-->>Composer: Bounded candidate context
    Composer->>SQL: Acquire transaction-owned budget lock, check daily quotas and leases
    alt No candidates / unavailable budget / quota exhausted
        Composer-->>SPA: 422 / 503 / 429 - no inference authorized
    else Reservation committed
        SQL-->>Composer: Durable request/token reservation + lease
        Note over SQL,Model: SQL budget transaction ends before model call
        Composer->>Model: Bounded goal + approved context, deadline, no automatic SDK retries
        Model-->>Composer: Proposal or explicit failure
        alt Provider failure / timeout / invalid references
            Composer-->>SPA: 502 - charge and lease retained
        else Valid proposal
            Composer->>Composer: Validate task IDs / selected target pairs
            Composer->>SQL: Recheck candidate publication and complete reservation
            Composer-->>SPA: 200 proposal if recheck/completion succeeds
        end
    end
    SPA-->>Person: Review proposal, nothing saved
    opt Writable capability and explicit human decision
        Person->>SPA: Open prefilled ordinary editor and save
        SPA->>Gate: Separate authorized catalog write
    end
```

The completion/recheck path can also fail explicitly; it does not manufacture
a successful proposal after losing the budget store. Public runtime is
proposal-only; read-only collection capabilities prevent the editor handoff from
becoming authoring there. Private Composer uses its own per-user request-log
rate limiting and selected catalog snapshots, rather than this anonymous
database-wide budget flow.

## 4. Optional task attachment write and compensation

```mermaid
sequenceDiagram
    actor Author
    participant Gate as Worker / PlaygroundWriteGuard
    participant Tasks as Tasks handler
    participant Files as BlobStorageService
    participant Blob as Selected Blob account
    participant SQL as AppDbContext / SQL
    Author->>Gate: POST or PUT task multipart metadata + optional file
    Gate->>Tasks: Admit authorized private write or explicitly protected Playground
    Tasks->>Tasks: Scope, item permission, fields, file type/size and update concurrency
    opt Attachment included
        Tasks->>Files: UploadAsync(collection, taskId, file)
        Files->>Files: Reject isolated curated write, sanitize name and path
        opt Playground attachment
            Files->>Blob: Reserve cumulative bytes in budget blob using conditional ETag
            Blob-->>Files: Reservation or exhausted/busy failure
        end
        Files->>Blob: Upload to unique staged path
        Blob-->>Tasks: New attachment path
    end
    Tasks->>SQL: Save metadata and new path, collection stamping / SQL constraints
    alt Rejected supported database write
        Tasks->>Files: Delete staged new attachment if present
        Files->>Blob: Targeted compensation
        Tasks-->>Author: Conflict / explicit failure
    else Metadata saved
        opt Replaced prior attachment
            Tasks->>Files: Delete previous attachment path
            Files->>Blob: Targeted old-blob cleanup
        end
        Tasks-->>Author: Created / updated task
    end
```

SQL and Blob are not one distributed transaction. Unique staging avoids
overwriting an old file before a metadata write succeeds; supported rejection
paths attempt targeted cleanup. Failures/crashes around either store can still
require operator reconciliation. Playground reservations are cumulative and are
never refunded, including after cleanup or uncertain uploads.

Task deletion snapshots references in surviving workflow steps, deletes the
attachment if present and saves the record/step changes. A later SQL failure
cannot undo an already deleted Blob. This is a real consistency trade-off, not
an architecture guarantee of atomic delete across two services.

## 5. Reviewed catalog import

```mermaid
sequenceDiagram
    actor Operator
    participant CLI as Import Program
    participant Import as CatalogImportService
    participant SQL as Explicit SQL destination
    Operator->>CLI: Reviewed JSON + chosen target + existing directory actor
    CLI->>Import: ImportAsync, dryRun / publishReviewed options
    Import->>Import: Bound bytes/counts, validate envelope, IDs and same-collection references
    Import->>SQL: Verify actor exists and begin serializable transaction
    Import->>SQL: Read dataset receipt / destination state
    alt Same content and unchanged previously imported state
        Import->>SQL: Roll back read transaction
        Import-->>Operator: AlreadyImported
    else Populated destination / changed receipt or state
        Import-->>Operator: Refuse, never reset destination
    else Empty valid destination
        Import->>Import: Reuse method/task/workflow/registry validators
        Import->>SQL: Save methods, tasks, workflow steps and receipt / state hash
        alt Dry run
            Import->>SQL: Roll back
            Import-->>Operator: Validated dry-run counts
        else Explicit real import
            Import->>SQL: Commit
            Import-->>Operator: Imported counts
        end
    end
```

`publishReviewed` sets publication state within this authorized import, not an
HTTP permission. The CLI does not create directory users or provision SQL
access. Owner-supplied packages must satisfy its contract; it is not a generic
overwrite/import-anything endpoint.

**Evidence:** [HTTP client](../../../frontend/src/services/apiClient.ts),
[auth recovery](../../../frontend/src/auth/AuthProvider.tsx),
[MCP transport](../../../api/src/Functions/Mcp.cs),
[MCP adapter](../../../api/src/Services/Mcp/GalleryMcpTools.cs),
[Composer](../../../api/src/Services/PublicComposer.cs),
[budget transactions](../../../api/src/Services/PublicComposerBudget.cs),
[task write lifecycle](../../../api/src/Functions/Tasks.cs),
[Blob routing](../../../api/src/Services/BlobStorageService.cs),
[import transaction](../../../api/src/Services/CatalogImportService.cs).
