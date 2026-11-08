# Public techniques catalog

Original, publication-safe demonstration content for the **public-techniques**
collection only. It is not the separately curated internal-tools collection.

The checked-in corpus contains **225 tasks/assets, 37 ordered workflows,
15 methods and 44 retrieval/Composer golden intents**, grounded in **29 public
primary documentation pages fetched and reviewed on 5 November 2026**. Multiple
techniques share a source; these are not 224 independent source discoveries.

## What is included

There are 20 individually authored jobs in each of research, writing, meetings,
spreadsheet/data analysis, presentations, coding, testing, knowledge curation,
Cowork-assisted work design and Scout desktop task design, plus 25 extension
examples/references. Each job specifies applicability, inputs, a distinct
procedure, expected output and review criteria. The dataset intentionally includes
planning/review tasks: importing it does not run a desktop agent or scheduler.

The extension set distinguishes ten artifact kinds and provider-specific formats.
It includes original instruction-only skills, instructions, a VS Code prompt
file, a GitHub custom-agent draft, a minimal Claude plugin manifest and an
illustrative individual MCP tool schema. Other entries are explicitly
**reference-only** design checklists, not invented downloadable native manifests
or implemented services. A builtin Researcher reference is a prompt, not a fake
agent package.

Tasks target the eleven reviewed execution experiences; Microsoft app/data
locations remain context tags, not unsupported artifact hosts. Workflows specify
one actual target per catalog-backed step. Frontier denotes preview access,
not a runtime. Microsoft Copilot Cowork is not Anthropic Cowork. Plugin packages,
hook configuration and provider-specific metadata are not interchangeable.

All connections are **not verified**, and no example is installed. Product
documentation supports a technique, not a live gallery connection, user license,
tenant availability or successful execution. No rating, identity, customer
record, secret, contact, private URL or real organizational content is seeded.
The second collection must remain empty until publication-safe content is
explicitly selected for it.

## Authored source and deterministic build

- `recipes.mjs`: 200 individually authored jobs; each row describes one concrete
  goal rather than a generated permutation of products and formats.
- `extensions.mjs`: 25 provider-labeled artifacts and reference procedures.
- `methods.mjs`: 15 distinct approaches linked from tasks and workflows.
- `journeys.mjs`: 37 editorially ordered sequences and 44 user-intent queries.
- `sources.mjs`: public URL, title, source date where known, checked date and an
  original review summary. Unknown publication dates stay null.
- `catalog.json`: deterministic generated envelope consumed by import tooling.
- `catalog.test.mjs`: offline editorial, provenance, reference and safety checks.

From the repository root:

```powershell
node datasets\public-techniques\build.mjs
node datasets\public-techniques\build.mjs --check
node --test datasets\public-techniques\catalog.test.mjs
```

Changing the authoring modules requires regenerating the JSON. Stable fixture
slugs produce namespaced deterministic UUIDv8 identifiers; reordering authored
rows does not change entity IDs. `datasetId: public-techniques-v1` and
`collectionId: public-techniques` are explicit import boundaries.

The source cache stores concise original review notes, not redistributed vendor
documentation. No long published example, prompt library or vendor source code
was copied. Source material retains its own rights; no third-party license is
asserted. Review the current public pages again before making fresh product
compatibility or availability claims.

## Import and verification boundary

The Node checks validate editorial content, counts and references; they are
**not a replacement for the application's actual write/registry validation**.
The importer must use that validation before writing to an isolated fresh schema.
It must require an explicit target collection and an existing real maintainer
principal, support a no-write dry run and use a transaction plus a dataset marker
for idempotency. Foreign or edited content must never be overwritten, removed,
reset or silently adopted. No anonymous publication endpoint is authorized by
this dataset. Live UAT import is a separately approved maintainer operation.

The maintainer console under `import` deliberately has no default connection
or write mode. Set a connection string through your normal local secret handling
in a dedicated environment variable; do not put it in a command, this corpus or
source control. After the application schema and real maintainer identity
already exist in the chosen isolated database, inspect help and run a dry run:

```powershell
dotnet build datasets\public-techniques\import\PublicTechniques.Import.csproj --configuration Release
dotnet run --no-build --configuration Release --project datasets\public-techniques\import -- --help
dotnet run --no-build --configuration Release --project datasets\public-techniques\import -- --catalog datasets\public-techniques\catalog.json --collection public-techniques --connection-env PUBLIC_TECHNIQUES_IMPORT_CONNECTION --target-database <exact-isolated-database-name> --actor-id <existing-maintainer-user-guid> --dry-run
node --test datasets\public-techniques\import-cli.test.mjs
dotnet test api\tests\WorkflowGallery.Api.Tests.csproj --filter FullyQualifiedName~PublicTechniquesCatalogTests
```

Only replace `--dry-run` with `--apply` after separately approving the exact
target. Content remains unpublished by default. To preview and then import
already publication-reviewed examples as public, explicitly add
`--publish-reviewed` to both dry-run and apply invocations. This flag is a
maintainer approval boundary, never an anonymous write capability. Publication
mode is part of the idempotency receipt; changing it is not an implicit update
to previously imported content.

The console verifies the connection's database name and the envelope's
dataset/collection before invoking the shared application importer. It never
runs migrations, creates users or activates content. Failed or uncertain imports
must be reconciled using local database diagnostics before a retry; credentials
and raw provider exception details are not printed.

The dataset integration suite uses the **actual** shared importer, registry,
field validation, file rules, candidate selector and public task functions with
fresh in-memory SQLite databases. It checks dry-run rollback, idempotency
receipts, existing-principal requirements, foreign-content preservation,
invalid references/combinations and collection boundaries. A failure injected
after intermediate database inserts verifies transaction rollback, not just
preflight rejection. Public-read cases verify unpublished content stays hidden,
the second collection stays empty and anonymous publication is denied. These
isolated test principals are not included in the corpus or created by the CLI.

Golden scenarios name expected task, method and related workflow IDs and hard
target/kind/format constraints. They test candidate retrieval and composition
inputs, not exact nondeterministic model wording. Related workflow expectations
are recommendations for the intent, not claims that each workflow contains the
expected individual task. Live model calls and deployed authentication are not
established by offline checks.
