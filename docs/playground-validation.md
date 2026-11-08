# Playground validation and artifacts

This index separates source-level checks from hosted evidence. Passing local tests or
compiling the deployment template does not prove that SQL triggers, grants, blob roles,
or anonymous Playground writes are active in Azure.

## Local and offline evidence

The source validation command is:

```powershell
node scripts\validate.mjs all
```

It runs catalog/dataset checks, API tests, frontend lint/tests/build, browser tests, and
reference-client tests. The standalone public deployment-helper test is:

```powershell
pwsh -NoLogo -NoProfile -File scripts\test-deploy-public-demo.ps1
```

That helper compiles the actual Bicep template and evaluates offline safety assertions;
it makes no Azure calls. Bicep can also be compiled directly with:

```powershell
bicep build infra\public-demo.bicep --stdout
```

| Local check | Result and boundary |
|---|---|
| API Release suite | 587 passed; includes scoped REST/MCP, admission, file handling, six durable upload-budget cases and existing private behavior |
| Frontend | 344 unit tests and six branding tests passed; lint and production build passed |
| Private-profile browser mocks | 36 desktop/mobile cases passed; not live Entra acceptance |
| Public-profile browser mocks | 20 desktop/mobile cases passed, including anonymous method create/edit/delete, optimistic version forwarding, collection switch and authoring-route denial |
| Reference client | 22 passed, including SDK/HTTP collection-scoped read verification |
| Reset source contract | Three passed; checks preflight ordering and scope, not SQL execution |
| Entra manifest helper | 24 passed with fictional fixture identities and an explicit manifest input |
| Public deployment helper | 210 offline assertions passed, with actual Bicep compilation and no Azure calls |

Catalog/dataset checks preserve the published 225-task paging corpus. The complete
`all` selector runs these through the repository's existing composed validator.
The immutable release manifest and retained command logs record the exact final
source revision and validation execution; generated TRX/build files are not source.

The final composed run used fresh browser ports and `CI=true`: the private browser
runner classified 35 cases as passed and one desktop workflow-method association
case as flaky (passed on its configured retry). The separate single-worker run
passed all 36 private cases without retry. All 20 public browser cases passed
without retry. Retain this timing caveat rather than presenting the composed run
as entirely retry-free.

The former API test stall was a test-fixture error: MCP transport calls lacked the
required matching collection scope, and a guard test waited for a request that could
not enter its handler. Rebuilding the corrected fixtures resolved the stall. Positive
Playground browser coverage also found and fixed a missing third build-time branding
preset that otherwise prevented the Playground from rendering.

Independent whole-change review identified host-account role overlap and unaccounted
staged uploads. Source fixes isolate Playground in a separate account and reserve a
cumulative 512 MiB with an ETag-protected blob counter before uploading. A narrow
follow-up caught a configuration fallback; public writes now require the dedicated
identity and separate endpoint even when a main storage connection string is present.
The reset SQL also received a separate narrow review with no significant findings.
These are static reviews, not deployed role or SQL acceptance.

The static reset-contract test checks that `reset-playground.sql` enters a serializable
transaction, rejects bidirectional cross-collection workflow/task/method/comment/rating
references before its first delete, and scopes every delete to Playground. This checks source
shape only; it is not SQL Server execution or proof of deployed trigger/grant behavior.

## Final release acceptance (7 November 2026)

The public frontend's final task detail layout was exercised against the deployed
custom domain at desktop and mobile widths. Field headings render as blocks, values
start below their headings, and neither viewport has horizontal document overflow.
Public Techniques and Curated Tools remain read-only. The custom-domain deployment
also reports Playground writes disabled; sample write acceptance does not enable it.

On the separately approved disposable sample, a synthetic task with a **one-byte**
text attachment was created (201), downloaded (200; exact byte verified), then deleted
(204). Subsequent task and file reads both returned 404 and the list reference was
absent. The cumulative upload budget is not refunded. These are API lifecycle checks,
not an independent storage-account enumeration.

The hosted publisher has been removed from source. Cowork is delivered as a
skills-only package and description, with organization approval and entitlement
checks remaining prerequisites; no organization import was performed. Cross-user
private acceptance and feedback delivery are deferred from this release.

## Earlier coordinator-provided live evidence

The coordinator supplied acceptance evidence for the deployed `4c5a906` paging/quota
base: desktop and mobile traversals covered 225 tasks, 37 workflows and 15 methods;
published reads and 23 REST/MCP checks passed; and the deployed frontend matched the
reviewed package hashes. This evidence applies to published browsing and the existing
read-only public profile only. It does **not** show that Playground SQL/storage
protections or write operations have been deployed or activated.

There is no hosted CI result for this feature revision. This worktree has not pushed to
either repository; the coordinator owns publication and subsequent CI.

## Disposable Playground sample acceptance (7 November 2026)

The separately hosted disposable sample is not the custom-domain public demo:
the public target remains empty and read-only. Environment-specific sample
identifiers are intentionally omitted from this public-source document.

On the disposable sample only, the Playground write flag was explicitly enabled
after verifying its separate storage account/container, scoped blob writer, SQL
guards and grants. The Bicep default remains disabled. The anonymous session
reported `canWrite=false` for Public Techniques and Curated Tools, and
`canWrite=true` with `isDisposable=true` for Playground. Desktop and mobile
acceptance confirmed authoring controls appear only in Playground. A synthetic
task was created, edited and deleted; the sample returned to its prior empty
Playground state. No public or curated records were changed.

Attachment upload and removal were **not** live-tested. Playground uploads reserve
the cumulative blob-budget counter without refunds, even when an upload is later
replaced or deleted; do not perform a live upload until that non-refundable
capacity use is explicitly approved. This acceptance did not deploy the publisher
or validate its hosted Entra/SQL/Blob path.

## Not performed during the earlier sample acceptance

- No live attachment upload or removal was performed.
- No Playground SQL policy apply/disable/reset, grant or role change was performed
  during the 7 November sample acceptance; it used the already reviewed sample setup.
- No writes were made to Public Techniques, Curated Tools, or separate
  user-acceptance environments.
- No publisher deployment or hosted Entra/SQL/Blob acceptance was performed.
- No model inference, reservation-ledger operation, or Composer quota change occurred.
- No source was uploaded to or published from the new public repository.

The SQL apply/disable/reset scripts under `deployment/playground/` are reviewed
provisioning artifacts only. Their SQL Server behavior, database grants, trigger
coverage, and storage isolation still require a separately approved data-plane review
and live verification. `reset-playground.sql` was not run during the 7 November
sample acceptance.

In particular, verify both directions of workflow-step/workflow/task/method references,
reverse comment/rating references, concurrent reset behavior, and the original published
hashes. Verify the separate account's effective role assignments and actual Blob ETag
contention/uncertain-outcome behavior. The cumulative upload counter must never be
refunded or reset independently; a full reset requires disabled/drained writes and
clearing the entire dedicated container, including the counter.

## Release packages

The immutable API and public SPA packages are kept outside the repository in the
session artifact directory. Their release manifest records the exact source commit,
archive SHA-256, byte/entry counts, per-entry hashes, build commands, and package
structure checks. API archives include `.azurefunctions` worker content and root
Functions metadata; SPA archives include the root `staticwebapp.config.json`. ZIP
entries use portable relative `/` paths. Rebuilds must use a new artifact directory
and filenames rather than replacing an existing package.
