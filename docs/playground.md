# Public collections and Playground

The public profile has exactly three collection scopes in one installation. This is a
collection-capability model, **not** customer tenancy, organization membership or private
workspaces. Feature 010 remains specification-only.

| Collection | Public reads | Anonymous authoring | MCP tools |
|---|---|---|---|
| `public-techniques` | Published records only | No | Eight existing read tools |
| `curated-tools` | Published records only | No | Eight existing read tools |
| `playground` | Shared content, including drafts | Existing catalog CRUD, uploads, comments and ratings, behind an explicit default-off switch | Eight reads plus twelve existing authoring tools when separately enabled |

Playground content is **shared, public and disposable**. Do not put private information,
credentials, internal tools or confidential customer data there. Catalog workflows are
documents describing ordered human/tool steps; they are not executable agents or an
automation runtime. Anonymous Playground access does not include feedback/email, administration,
model inference or external tool execution.

## One scoped capability contract

Every REST and MCP operation resolves one validated `collectionId` into request-scoped
capabilities. Missing, duplicated, unknown or mismatched scope fails closed. Public/curated
writes are refused by the API, not merely hidden in the interface. Collection filters,
write stamping and reference checks prevent IDs from one collection being used in another.
The deployment switch can only reduce the capability:

```text
PublicDemoEnabled=false                  -> private Entra profile and existing permissions
PublicDemoEnabled=true, writes=false     -> published reads plus Playground reads
PublicDemoEnabled=true, writes=true      -> Playground writes only, after DB/storage review
```

Anonymous requests remain unauthenticated. A single reserved sandbox actor is used only for
catalog attribution; its directory tenant/object IDs are null and it is not an Entra principal.
The UI identifies Playground content as shared/public/disposable and switches its editors and
write actions from the returned collection capabilities.

The public read-only MCP connections for `public-techniques` and `curated-tools` retain exactly
the eight existing read tools. The separately selected Playground connection can discover and
call the eight reads plus twelve existing catalog write tools only when public MCP and the
Playground write capability are both enabled. Tool schemas bind `collectionId` to the selected
connection, and both discovery and execution use the same policy. `EnableMcpWrites` alone never
enables anonymous writes. See the [MCP guide](connect-production-mcp-server.md).

## Data and file isolation

The application reuses the existing REST handlers, validation and optimistic concurrency. EF
query filters and save-time collection stamping are application safeguards; they are not the
database authorization boundary. The reviewed SQL policy artifact adds collection guards and
same-collection reference checks before object-level DML grants. It also checks for a runtime
principal with neither `db_owner` nor `db_datawriter`, uses a transaction-owned admission lock,
and bounds Playground catalog/engagement rows to 5,000 and saved attachments to 512 MiB.

Playground uploads use the dedicated `playground-files` container in a **separate storage
account** and a separate storage identity. The host runtime's account-level role does not
cover that account. `PlaygroundStorageBlobEndpoint` must differ from the main blob endpoint;
public/curated attachments remain on the existing container. API validation remains in force,
and write admission is limited to 60 requests/minute and four active writes per worker, with
256 KiB catalog JSON and 6 MiB task multipart request limits. Those in-process request limits
multiply under scale-out; the SQL admission lock and stored-size/row checks coordinate the
database limits across workers.

A separate blob ETag counter reserves a cumulative **512 MiB upload budget per sandbox reset**,
across all workers, before any file bytes are uploaded. Failed, cancelled, replaced, deleted
and orphaned uploads retain their reservation. There are deliberately no automatic refunds:
uncertain outcomes must not allow unaccounted growth. Exhausted or contended reservations
return 429; a malformed counter fails closed. This bounds attempted attachment bytes, not just
saved SQL rows, at the cost of requiring an operator reset sooner. It does not modify the
Composer ledger. Blob versions, snapshots and soft-delete retention must remain disabled.

The SQL and storage artifacts are review-only. A data-plane operator must check the exact
target, apply schema protections and scoped grants, and verify those grants before setting
`PublicPlaygroundWritesEnabled=true`. The source default is false. Do not use database ownership,
blanket writer access, the fresh-only deployment helper or application startup to apply the
change.

`deployment/playground/disable-playground-policy.sql` disables the new write surface without
deleting data or touching the Composer ledger. `reset-playground.sql` requires an explicit
`RESET-PLAYGROUND` confirmation, runs a serializable preflight, and refuses the reset if it finds
cross-collection workflow-step, method, comment or rating references in either direction. The
preflight runs before the first delete and within the same transaction, so the script cannot
cascade-delete or null a non-Playground workflow step. Clear only the dedicated Playground blob
container (including its upload-budget counter) in a separately reviewed operation **only
while writes are disabled and all in-flight requests have stopped**. Never delete or decrement
the counter independently of clearing every Playground blob; neither artifact may reset public/curated content
or Composer charges. A static source test checks that these guards precede every Playground-scoped
delete; it does not execute or validate the script against SQL Server.

## Workflow composer

Visible **Workflow composer** labels replace the former AI Composer wording; API routes and
protocol names remain stable. Generation produces a proposal for human review, never a saved
workflow. The person edits/accepts the draft and explicitly uses the normal workflow editor's
save action. That save follows the selected collection's ordinary validation, capability and
concurrency path. Changing collections clears or re-scopes draft/cache state; a proposal cannot
be automatically committed by generation.

The composer does not execute the workflow or any referenced agent, skill, plugin, MCP server,
or external service. In public collections, it returns proposals only: the composer does not
offer a handoff that creates a workflow or task, even in the writable Playground. Authorized
private users can still review a proposal in the editor and explicitly save through the normal
collection validation and concurrency path. Public model usage remains separately budgeted and
is not required for Playground CRUD. Its configured daily request/token limits, reservation
ledger, concurrency, charges and refund behavior are not changed by this feature.

## Diagrams and rollout evidence

- [Public architecture and trust boundaries](diagrams/public-playground-architecture.svg) ([Mermaid](diagrams/public-playground-architecture.mmd))
- [Collection request and MCP flow](diagrams/collection-request-flow.svg) ([Mermaid](diagrams/collection-request-flow.mmd))
- [Workflow composer proposal/save flow](diagrams/workflow-composer-flow.svg) ([Mermaid](diagrams/workflow-composer-flow.mmd))
- [Artifact, checksum and evidence index](playground-validation.md)

The included source work does not claim that public Playground writes have been activated or
live-tested. The independent coordinator's live release evidence for base `4c5a906` covers
paging/quota behavior and published read-only browsing only; it is not evidence that these SQL
triggers, storage grants or Playground CRUD are deployed. Private Entra access is governed
separately. See the validation index for evidence levels and gaps.
