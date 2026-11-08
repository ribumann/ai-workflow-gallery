# Frontend contributor guide

The AI Workflow Gallery frontend uses React, TypeScript, Vite, React Router and
Fluent UI React v9. It supports discovery and editing of tasks, workflows and AI
methods, a reports/dashboard directory, integration guides and an optional Workflow composer.
Start with the [repository overview](../README.md) and
[architecture](../docs/architecture.md) for the wider system.

## Local setup

Use **Node.js 24 LTS and npm** for the locked toolchain. The root README's broad
older Node 20+ prerequisite was not sufficient for every patch version: dependencies in
`package-lock.json` have narrower engine requirements. Install from the lockfile;
do not regenerate it just to work on documentation or tests.

From the repository root, in PowerShell:

```powershell
cd frontend
npm ci
npm run dev
```

If PowerShell blocks `npm.ps1` under its execution policy, use the standard
`npm.cmd` launcher for these commands; no execution-policy change is needed.

Vite serves the registered development origin `http://localhost:5173` and fails if that
port is occupied rather than silently changing the OAuth redirect. It proxies `/api`
to the local Functions host on port 7071, without synthesizing identity or tokens.
**A SPA server alone is not an authenticated API.**

For the full local app, follow [root local development instructions](../README.md#local-development):

1. Prepare .NET 10, Azure Functions Core Tools v4, local SQL Server and Azurite.
   Copy [the API settings template](../api/src/local.settings.json.template) to
   `api\src\local.settings.json`, supply local connection strings and organization
   settings, and keep credentials out of Git. The template enables development seed
   data; use a development database, never a production connection for this walkthrough.
2. In a separate terminal at `api\src`, run `dotnet restore` then `func start`
   (normally `http://localhost:7071`). Keep the Vite terminal running.
3. Copy `.env.example` to `.env.local` and configure the registered SPA/API client GUIDs.
   Open `http://localhost:5173`, whose exact origin must be a registered SPA redirect.
   Use provider-managed Entra sign-in/consent; SWA CLI identity emulation is not trusted.

The frontend uses same-origin `/api/*` requests. MSAL Browser handles authorization-code PKCE
and adds delegated bearer tokens; the API validates the cryptographic token, admitted
directory, human identity and scope. [AuthProvider](src/auth/AuthProvider.tsx) distinguishes
signed-out, denied and unavailable states. Later expiry offers popup recovery without
discarding the open draft or replaying writes. No browser cookies are extracted or stored
in MCP configuration.

The Workflow composer additionally needs API-side AI Foundry settings, credentials and
resource permissions. Without that configuration its API returns 404; the rest of
the app remains usable. See the [proposal-versus-save contract](../docs/playground.md#workflow-composer)
and [deployment configuration](../docs/public-demo-deployment.md).
Do not add browser credentials or weaken the auth guard to make a local demo work.

## Deployment branding

[branding.json](branding.json) is the single **public, build-time** identity source for
the HTML title (before React starts), Fluent shell/theme, sharing choices and generated
integration downloads. The neutral default needs no organization, assets or footer links.
Absent links are omitted; there is no original-maintainer fallback. Branding does not
change catalog data, routes, authentication, directory admission or feedback delivery.

Copy [branding.example.json](branding.example.json) to a deployment-specific JSON file
and edit its example values. From `frontend`, select it for **both** downloads and Vite:

```powershell
$env:GALLERY_BRANDING_FILE = 'branding.example.json'
npm.cmd run build
Remove-Item Env:GALLERY_BRANDING_FILE
```

Paths are relative to `frontend` (absolute file paths also work). Replace `example.org`
with your own reviewed URLs before publishing; the sample is a rendering fixture, not
a legal policy. Rebuild to change branding; no source-code changes or admin UI are needed.
Only explicitly allowlisted fields are accepted; errors stop startup/build/downloads.

| Field | Contract |
| --- | --- |
| `title` | Required nonempty text, at most 80 characters (30 for Cowork publication). |
| `organization` | Optional secondary text, at most 80 characters. |
| `logo`, `favicon` | Optional root-relative paths to existing, well-formed, self-contained SVG files inside `public`. Remote assets, scripts, external resources and embedded stylesheets are rejected. The header logo is decorative beside the accessible title; avoid putting essential information only in the image. Package icons remain generated PNGs. |
| `accent` | Optional `#RRGGBB`, at least 4.5:1 contrast against white; generates a Fluent light-theme brand ramp. Default is Fluent's incumbent light theme. |
| `links` | Optional `support`, `privacy`, `terms`, `source` absolute HTTPS URLs without embedded credentials. Absent entries are not rendered. |
| `shareChannels` | Optional nonempty unique list of `copy`, `email`, `teams`; default preserves all three. No messages are sent by this configuration. |
| `publisher` | Optional complete `name` (at most 32 characters), `website`, `privacy`, `terms` HTTPS URLs. Required to emit the Cowork ZIP, because its schema requires actual publisher/legal URLs. |

Never put credentials, email recipients, tenant policy or other private fields here:
all accepted values are public and can appear in the browser or downloaded instructions.
Feedback recipients and delivery configuration remain server-only; its neutral subject
is intentionally independent of this public display config.

Without `publisher`, the normal skill ZIP, SKILL.md and references still build. The
Cowork ZIP is not emitted (a stale generated ZIP is removed), and its guide explains the
missing publisher configuration instead of linking a nonexistent or invalid package.
With a complete publisher, the existing `/downloads/ai-workflow-gallery-cowork-plugin.zip`
URL, manifest schema/ID and folder shape remain unchanged. Downloads are instructions,
not proof that tenant installation or native MCP connectivity works.

The typed public `Branding` contract and `createGalleryTheme(preset)` factory are
exported from `src/branding.ts` for a collection-selection layer to reuse. Validate each
complete public preset with the existing build-time `validateBranding` helper before
projecting it; do not copy the palette generator or pass catalog/user data as branding.
The factory does not select collections, enable public access or change deployment
metadata/downloads. Public collection presets are validated at build time in
`collection-branding.json` for all three collections.

## Commands

Run these **inside `frontend`**; [package.json](package.json) is the source of truth.

| Command | Purpose and boundary |
| --- | --- |
| `npm run dev` | Vite on registered port 5173 with `/api` proxy to 7071; no identity emulation. |
| `npm run build` | Runs `prebuild` downloads generation, then `tsc -b` and Vite; emits `dist`. |
| `npm run preview` | Serves an existing `dist` build, normally on port 4173; not an API or SWA host. |
| `npm run downloads` | Generates integration packages into ignored `public\downloads` from repository `integrations` sources. Also runs before `build`, but not before `dev`. |
| `npm run lint` | ESLint for `src`; does not lint the test directory. |
| `npx prettier --check .` | Checks formatting without rewriting files. |
| `npm run format` | Rewrites formatting throughout the frontend; avoid unrelated formatting churn. |
| `npm test` | Runs the Vitest suite once. |
| `npm run test:watch` | Starts interactive Vitest watch mode. |
| `npm run test:e2e` | Runs Playwright desktop and mobile Chromium projects. |

On a new Playwright installation, provision Chromium with `npx playwright install chromium`.
[Playwright config](playwright.config.ts) builds and starts a preview server on port
4173 by default; `PLAYWRIGHT_PORT` overrides it. Outside CI it can reuse an existing
server, so avoid leaving an unrelated server on that port.

## Where to work

| Location | Responsibility |
| --- | --- |
| [src/main.tsx](src/main.tsx), [src/App.tsx](src/App.tsx) | React root, browser routing, Fluent theme, authentication gate and routes. |
| [src/auth](src/auth) | MSAL PKCE, session state, explicit recovery and authorization guard. |
| [src/components/AppShell.tsx](src/components/AppShell.tsx) | Responsive navigation, feedback entry point and AI composer panel. |
| [src/pages](src/pages) | Discovery, task/workflow/method editors and details, reports and integration guides. |
| [src/components](src/components) | Shared Markdown editor/renderer, method selection, engagement and other UI. Markdown rendering uses `marked` and DOMPurify. |
| [src/services/apiClient.ts](src/services/apiClient.ts), [types.ts](src/services/types.ts) | Typed API requests, error statuses and shared request/response shapes. |
| [src/utils](src/utils) | URL checks and embed hints, device identity, security-rejection and version-copy helpers. |
| [tests/unit](tests/unit), [vitest.config.ts](vitest.config.ts) | Vitest + jsdom + Testing Library; shared setup is `tests\unit\setup.ts`. Tests are outside the production TypeScript project's `src` include. |
| [tests/e2e](tests/e2e), [playwright.config.ts](playwright.config.ts) | Browser journeys with intercepted session/API responses. |
| [scripts/build-integration-downloads.mjs](scripts/build-integration-downloads.mjs) | Builds downloadable integration files from [integrations](../integrations). Generated downloads are not source files to edit. |

Discovery uses `/?tab=tasks`, `/?tab=workflows` and `/?tab=methods`; `/methods`
redirects to the methods tab. Reports live at `/reports` and integration guides at
`/integrations/:id`. See `App.tsx` for editor/detail routes and legacy redirects.

## What local checks do not prove

Unit tests use jsdom and test doubles. Existing Playwright journeys intercept
`/api/session` and backend responses: a passing browser test demonstrates the UI
against fixtures, **not** Entra login, API persistence, SQL/Blob connectivity,
authorization enforcement, AI Foundry access or live multi-user behavior.
Use the [validation and acceptance boundaries](../docs/playground-validation.md)
and feature-specific quickstarts for authorized end-to-end acceptance.

Report previews are also best-effort: the destination's permissions, sign-in,
third-party-cookie policy and framing headers still apply. URL hints cannot prove
that a report will render, and gallery sign-in grants no destination access. Use
supported embed URLs or open externally; do not bypass destination security.
# Curated publication

This release has no hosted Curated Tools publisher or publisher sign-in.
Curated content is supplied as an independently reviewed import package; see
[manual publication](../docs/public-demo-deployment.md). Public visitors cannot
publish content or write curated attachments.
