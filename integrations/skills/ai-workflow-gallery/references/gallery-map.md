# Gallery page and URL map

Companion reference for the `ai-workflow-gallery` skill. Load this when you need to know exactly
where something lives or what a form asks for.

All paths are relative to the environment base URL:

Use only the explicitly chosen deployment URL. This personal deployment has fresh UAT
with synthetic data and no production environment. Do not substitute legacy enterprise URLs.

## Left-hand navigation

| Group | Items |
|---|---|
| **AI Workflows** | Tasks, Workflows, AI Methods |
| **Reports & AI Dashboards** | All links, Reports, AI Dashboards |
| **Tool integration** | GitHub Copilot, Cowork, Scout |

The header also holds an **AI Assistant** panel and a **Feedback** button.

## Pages

| Path | What it is |
|---|---|
| `/` | Discovery page. Tabs: **Tasks**, **Workflows**, **AI Methods** |
| `/?tab=tasks` | Discovery page, Tasks tab |
| `/?tab=workflows` | Discovery page, Workflows tab |
| `/?tab=methods` | Discovery page, AI Methods tab |
| `/tasks/:id` | Task detail |
| `/tasks/:id/edit` | Task editor |
| `/tasks/new` | New task |
| `/workflows/:id` | Workflow detail |
| `/workflows/:id/edit` | Workflow editor |
| `/workflows/new` | New workflow |
| `/methods/:id` | AI method detail |
| `/methods/:id/edit` | Method editor |
| `/methods/new` | New method |
| `/reports` | Report and dashboard directory |
| `/reports?kind=Report` | Reports only |
| `/reports?kind=AiApp` | AI dashboards only |
| `/reports/:id` | Report link viewer |
| `/reports/new` | New report link |
| `/integrations/github-copilot` | Guide: connecting GitHub Copilot |
| `/integrations/cowork` | Guide: connecting Cowork (this skill) |
| `/integrations/scout` | Guide: connecting Scout |

## Search and filter controls

On the discovery page (`/`):

- **Search by keyword...** — filters as you type. It is **not** driven by a URL parameter, so
  navigate to the tab first and then type into the box.
- **All types** — dropdown filtering by task type.
- **All tool/surfaces** — dropdown filtering by the tool the entry targets.

On `/reports`:

- A keyword search box, and tabs for **Reports** and **AI Dashboards**.
- The `q` and `kind` values here *are* reflected in the URL, so `/reports?kind=Report` works
  directly.

## Task editor fields

| Field | Required | Notes |
|---|---|---|
| **Name** | Yes | Short, specific, recognisable in a list |
| **Description** | Yes | What problem it solves, when to use it |
| **Task type** | Yes | Dropdown |
| **Tool / surface** | Yes | Dropdown — the tool the task targets |
| **Tags** | No | Comma-separated, e.g. `reporting, automation` |
| **Reference link** | No | URL to source material |
| **Inline content** | No | The prompt or snippet itself |
| **Data source (optional)** | No | What the task expects as input |
| **Data output (optional)** | No | What the task produces |
| **Attached file** | No | The user must attach this themselves |

## Report link fields

| Field | Required |
|---|---|
| **Name** | Yes |
| **Description** | Yes |
| **URL** | Yes |
| **Kind** — Report or AI Dashboard | Yes |

## Behaviour to expect

- **Attribution.** Created and edited entries record the signed-in user's name automatically. There
  is no author field to fill in.
- **Concurrency.** If someone else edits an entry while you have it open, saving fails with a
  message saying so. Reload and re-apply the change.
- **Deletion.** Deleting asks for confirmation in a dialog. Never confirm one without the user
  saying so explicitly.
- **Session expiry.** The sign-in lasts about 8 hours. When it lapses, the site returns its sign-in
  page — hand the browser back to the user.
