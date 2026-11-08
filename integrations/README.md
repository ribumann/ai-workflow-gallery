# Integration packages

Downloadable artifacts that let an AI assistant work with the gallery. One skill source, three
packagings.

```text
integrations/
├── skills/ai-workflow-gallery/     # the skill — single source of truth
│   ├── SKILL.md
│   └── references/
│       ├── mcp-tools.md            # read when the MCP tools are available
│       └── gallery-map.md          # read when driving the website in a browser
└── cowork-plugin/
    └── manifest.json               # M365 unified manifest wrapping the same skill
```

## What gets built

`frontend/scripts/build-integration-downloads.mjs` runs as the frontend's `prebuild` step and
writes four neutral files and, when publisher configuration is complete, the Cowork ZIP
into `frontend/public/downloads/` (git-ignored — they are generated):

| Artifact | For | Contents |
|---|---|---|
| `ai-workflow-gallery-skill.zip` | GitHub Copilot, Scout | the `ai-workflow-gallery/` folder |
| `ai-workflow-gallery-SKILL.md` | per-user uploads | just the skill file |
| `ai-workflow-gallery-gallery-map.md` | per-user uploads | the browser-route reference |
| `ai-workflow-gallery-mcp-tools.md` | per-user uploads | the MCP tool reference |
| `ai-workflow-gallery-cowork-plugin.zip` | Cowork package review, only with configured publisher | manifest, generated icons, the skill folder |

The two reference files are published individually because a single-file upload path — Cowork's
Option A — cannot carry a `references/` folder. Without them, a user who took that route installed
a skill whose own "Additional resources" links resolved to 404, and the browser route ran with no
page or field map. Every `.md` under `references/` is published automatically, so adding a new one
needs no change here.

Rebuild locally with `npm run downloads` in `frontend/`. Icons are generated from the brand colour
rather than committed, and archives use a fixed timestamp, so identical sources produce
byte-identical output.

The [public branding configuration](../frontend/README.md#deployment-branding) supplies
display titles, publisher identity and the generated icon accent to both packages and
the app. `cowork-plugin/manifest.json` is a source **template**, not an upload-ready
manifest: the build adds the required developer fields only from a complete, validated
`publisher`. No privacy/terms policy or original-maintainer URL is invented. Without it,
the Cowork ZIP is omitted and a stale generated ZIP is removed. All other downloads keep
their existing URLs and skill identity. Installing a ZIP still requires the actual
client's documented support and tenant/account approval.

## Design notes

**The skill describes two routes, not verified compatibility on every surface.** It uses actual
connected gallery MCP tools or the authorized browser fallback. Native Copilot, Scout and Cowork
compatibility requires separate documented and live evidence; neither a package nor local config
proves reachability. The Cowork package declares no connector. MCP readiness is deployment-specific.
Use the [manual connection guide](../docs/connect-production-mcp-server.md) to check owner-confirmed
endpoint and capability details; these instructions do not enable MCP or authorize installation.

**Writes are gated, not merely discouraged.** `SKILL.md` carries six numbered hard stops:
confirmation before every write, no create without a completed duplicate search, quoted
before-and-after for edits to someone else's entry, no unrequested deletes, stop-and-ask on
ambiguity, and never fabricate. The skill creates and edits entries in a shared catalog under the
user's own name and the browser route cannot undo them, so advisory wording was not enough.

**One delegated authentication contract.** The API accepts verified Entra bearer tokens,
not captured browser credentials. The [registered reference client](reference-client/README.md)
uses maintained MSAL and the MCP SDK, keeps tokens in memory, and never replays writes.
Native-client interoperability must be verified for the actual registration and consent flow.

**The Cowork package declares no connector.** Supporting delegated OAuth in the API is necessary
but not sufficient to establish a cloud client's registration/resource compatibility. Until
verified, this package uses the authorized browser route. It carries no credentials or permissions.

## Constraints to respect when editing

- The skill folder name must equal the `name` in `SKILL.md` frontmatter, and must be kebab-case.
- Frontmatter takes only `name`, `description`, `license`, `compatibility`, `metadata` and
  `allowed-tools`. Keep `description` under 800 characters — it is the routing signal, and an
  over-long one fails validation.
- Bump `metadata.version` and add a [CHANGELOG](CHANGELOG.md) entry for any change that alters
  what the skill does. These artifacts get installed into other people's tools, where a silent
  change is hard to attribute.
- The manifest follows unified manifest v1.28, which is `additionalProperties: false` — any extra
  field fails upload.
- Keep the skill body under roughly 5,000 tokens; move detail into `references/`.
- Cowork allows at most 20 skills per package and 20 companion files per skill.

User-facing instructions live in `frontend/src/pages/Integrations/integrationGuides.ts`. Keep the
two in step: the guides name these exact filenames and folder layouts.
