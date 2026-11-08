# Catalog core contract

The fresh fork keeps `/tasks`, task downloads, ordered workflows, methods, reports,
engagement and delegated Entra authorization. It replaces the ambiguous product
enums with one reviewed [`registry`](../catalog/registry.json). It does not convert
provider packages, execute plugins, install connections or promise cross-provider
portability.

## Classification

Task metadata uses a registry kind ID in `taskType` and between 1 and 16 distinct
`targetIds`. Optional `format` must be supported for that kind by **every** selected
target. Omitting format means unspecified, not universal compatibility. `setup` and
`prerequisites` are optional contributor guidance, separate from the registry's
public access prerequisites. Unknown kinds, targets and combinations are rejected.

```json
{
  "name": "Review a proposed code change",
  "description": "Check evidence, scope and behavior before approving.",
  "taskType": "prompt",
  "targetIds": ["github.copilot.vscode", "github.copilot.cli"],
  "format": "text.plain",
  "setup": null,
  "prerequisites": "Provide the relevant diff and acceptance criteria.",
  "tags": ["review"],
  "referenceLink": null,
  "inlineContent": "Compare the change against its acceptance criteria. Report evidence and unresolved risks; do not approve or merge.",
  "dataSource": "A user-provided diff and requirements",
  "dataOutput": "A review for a human decision"
}
```

Send task metadata as the existing multipart `metadata` part. Attachments, bounded
content validation and optimistic concurrency remain in force. Search uses `type`
and `targetId`; aliases help the searchable **Works in** picker but persisted IDs
are exact, stable registry IDs.

The registry distinguishes execution, app/workspace context, output, authoring,
governance and access-program roles. Frontier is an access program, never an
execution host. Researcher and Analyst are built-in experience references, not
downloadable agent definitions. Declarative and custom-engine agent formats remain
separate. MCP server and MCP tool are separate kinds. Synced, federated and Power
Platform connector descriptions are distinct formats.

## Workflow steps

A catalog-backed step chooses one `targetId` from the referenced task's explicit
targets. Saving rejects a target that no longer belongs to the task. Live task
metadata remains authoritative; deletion retains last-known name/type and the
selected target, rather than deleting the workflow step.

A manual/review step has `isManualStep: true`, `taskId: null`, `targetId: null`,
a `manualStepTitle` and optional instructions/input/output. It has **no** invented
product or artifact type. Workflows remain ordered instructions, not an execution
engine.

## Evidence versus readiness

Each supported profile names a public primary source, review date and qualification.
Compatibility is a documented format/target statement. Access depends on license,
account, preview enrollment and administrator policy. Gallery readiness requires a
separate verified connection. None of those facts implies either of the others.
Private Entra authentication and consent boundaries remain separate from public
collection capabilities.

The opt-in anonymous public profile has exactly three collection scopes: published, read-only
`public-techniques` and `curated-tools`, plus shared, public and disposable `playground`.
Playground reads are available in the public profile. Anonymous Playground writes are gated by
a validated per-request capability and remain default-off until reviewed database and storage
protections are installed; they do not introduce customer membership or alter private Entra
permissions. See the [collection contract](playground.md).

## Fresh storage and publication boundary

Never point initialization at an existing or shared database. Startup must only accept the
fresh catalog schema marker; it must not call the legacy EF migration chain.
Initialization is explicit and only for an empty database. Existing incompatible
schemas fail with an actionable error, without backfill, reset or publication.

The public corpus is original example content grounded in public documentation,
not copied prompt libraries or internal/customer material. Its explicit collection
is `public-techniques`; `curated-tools` is reserved for separately reviewed,
publication-safe originals. Collection IDs scope catalog entities and references;
they are not new tenant permissions. Anonymous access is explicitly opt-in: set backend `PublicDemoEnabled=true` and
build the frontend with `VITE_PUBLIC_DEMO_ENABLED=true`. Private deployments keep
both settings false or absent and retain delegated Entra authorization. Public mode
permits published reads in the two published collections and draft reads in Playground.
It never authenticates an anonymous visitor. Playground authoring and engagement require
the separate write capability and reviewed SQL/storage protections; public MCP is an
independent opt-in. Feedback is unavailable in the public profile. Public Composer is
disabled by default and requires an explicitly opted-in, globally budgeted **read-only
proposal** path, as described below.

### Reproducible fresh schema

With the pinned .NET 10 SDK installed, print SQL offline from the repository root:

```powershell
dotnet build api\src\src.csproj --configuration Release
dotnet api\src\bin\Release\net10.0\src.dll --print-catalog-schema
```

No SQL connection, Entra registration or running Functions host is needed to print
the script. It includes the schema and the `CatalogSchemaMarker` row:
`Id = 1`, `Version = white-label-catalog-v4-playground-writes`. Printing is not an apply operation
and does not inspect any destination. An authorized operator must review the SQL,
confirm a dedicated new **empty** database and apply it through the approved
private-network administration path. Never apply it to an existing or shared database.

For disposable local SQL only, explicitly set `SqlConnectionString` to a new local
database and run the DLL command with `--initialize-catalog` instead. This path
refuses remote servers and any database containing user tables. Normal application
startup only verifies the marker and required fields with reads; it never creates,
migrates, seeds or resets the database.

The public-read runtime uses a separately provisioned contained managed-identity
user with read-only table access (`db_datareader` suffices); it needs no schema
ownership, `db_datawriter`, server/database administrator or import privileges.
If Playground authoring is separately enabled, the reviewed SQL policy grants only
catalog-table DML and enforces Playground-only changes with triggers; the runtime
must remain outside `db_owner` and `db_datawriter`. A dedicated Playground storage
identity is scoped to its file container. Provisioning these identities/grants is a
separate operator-reviewed step, not part of schema printing or startup. The guarded
corpus importer uses a separate write-capable operator principal; publication still
requires explicit `--publish-reviewed`.

### Optional bounded anonymous Composer

The public profile deliberately leaves inference disabled. Enabling it requires
`PublicDemoEnabled=true`, `EnableAiComposer=true`, `PublicComposerEnabled=true`,
`AiFoundryEndpoint` (HTTPS), `AiFoundryDeploymentName`, a reachable SQL ledger,
and **all** the numeric settings below. Missing/invalid values fail closed, not
back to private Composer. No dummy Entra app/client ID is needed in public mode.
Inference uses the runtime managed identity and an operator-provisioned,
account-scoped **Cognitive Services OpenAI User** role, never an API key.

| Backend setting | Conservative example | Hard maximum |
|---|---:|---:|
| `PublicComposer:DailyRequestLimit` | 10 | 100 |
| `PublicComposer:DailyTokenLimit` | 100000 | 2500000 |
| `PublicComposer:MaxConcurrentRequests` | 1 | 2 |
| `PublicComposer:MaxGoalCharacters` | 1000 | 1000 |
| `PublicComposer:MaxContextBytes` | 12000 | 16000 |
| `PublicComposer:MaxOutputTokens` | 1024 | 2048 |
| `PublicComposer:ModelTimeoutSeconds` | 20 | 30 |

Use `__` instead of `:` for environment variable names (for example
`PublicComposer__DailyRequestLimit`). Every numeric setting must be at least one,
and the daily token budget must cover at least one worst-case request.
The cap is token usage, not currency: deployment/model prices remain an operator
concern. Reserve **context UTF-8 bytes + 4 x maximum goal characters + 4096
system-prompt byte allowance + 1024 framing allowance + maximum output tokens**
before each call. Text token count is bounded conservatively by UTF-8 bytes;
output includes reasoning tokens. The app verifies the actual system prompt fits
the 4096-byte envelope or fails closed. There is one attempt, no SDK retry.
Goal/body/context/output and model deadline are independently bounded.

`POST /api/ai/compose?collectionId=...` accepts only `{ "goal": "..." }`.
The server ranks the whole selected collection's published metadata with the
existing candidate selector, discards nonmatching candidates, and fits relevant
items into the exact serialized UTF-8 context budget. It never sends the whole
catalog or accepts caller-provided context/IDs. Instructions/content/files are
not sent. Catalog descriptions and the goal are untrusted evidence, not system
instructions. The proposal resolves IDs/targets against the server-selected
context; publication is checked again after generation. No tasks are executed,
no integrations called, no identity invented, and no prompt/proposal history saved.
The UI hides catalog authoring and gap-creation actions in the proposal flow.

`dbo.PublicComposerReservations` is global across both collections, processes,
and instances. A short SQL transaction obtains an exclusive transaction-owned
`sys.sp_getapplock` **before** reading daily totals and active leases, then commits
the worst-case request/token reservation **before** inference. This preserves the
private Composer's reader-upgrade-deadlock fix. Inference holds no SQL transaction.
Known SQL deadlock victims retry reservation acquisition at most twice; unknown
commit results, lock timeouts, missing tables/permissions, and store outages deny
inference. SQLite uses an immediate write transaction for deterministic local tests.
There is no in-memory/IP/cookie quota and no client idempotency key.

Reservations are never refunded, including cancellation, timeouts, invalid model
output, unknown delivery, crashes, and failed completion writes. Every client retry
is a newly charged reservation before a new single model call. Daily limits reset
at UTC midnight, but unconfirmed concurrency leases span midnight. Confirmed model
responses release only the lease; unconfirmed failures retain a **300-second**
lease before capacity recovers. This bounds concurrent admitted calls, not
unobservable backend work after a timeout; provider-side termination cannot be
promised. The long lease is deliberately greater than the 30-second hard deadline.
Do not delete ledger rows to recover capacity; that would erase paid usage.

`GET /api/session` reports anonymous identity separately from `composerEnabled`,
`composerState` (`disabled`, `unavailable`, `quota_exhausted`, `busy`, `ready`),
and `composerMaxGoalCharacters`. Readiness checks the SQL budget, without reserving
or calling the model; it does not prove deployment health/model access. The POST
rechecks everything. Disabled proposals are 403; invalid configuration/store is
503; quota/concurrency exhaustion is 429 with `Retry-After`; no relevant published
items is 422; model failure is 502. None is a fabricated successful proposal.
Operational errors log error types/status, never prompts or model content.

The new fresh-schema marker includes and verifies the ledger. It is intentionally
incompatible with v1; there is no startup cloud DDL, reset, or old-data migration.
For the optional Composer, an authorized SQL operator adds only:

```sql
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.PublicComposerReservations
    TO [runtime-managed-identity-sql-user];
```

Replace the placeholder with the existing contained identity user. Keep catalog
reads (`db_datareader` or narrower existing grants). No ledger DELETE, schema
ownership, `db_datawriter`, or `db_owner` is needed. `sys.sp_getapplock` executes
under SQL's public database principal permission; no additional EXECUTE grant or
stored procedure is introduced. Schema creation and corpus import retain their
separate operator identities. Model provisioning, narrow role grants, live SQL
permission checks, and paid live acceptance are separate approved deployment
operations; mocked browser/model tests do not establish them.

Run `node scripts/validate.mjs catalog` for registry integrity and rejection cases.
The composed `all` runner includes this check together with API, frontend, browser
and reference-client coverage. Browser suites use mocked authentication and APIs;
they do not establish live Entra or cloud acceptance.
