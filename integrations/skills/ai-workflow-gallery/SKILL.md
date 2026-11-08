---
name: ai-workflow-gallery
description: |
  Find, follow and contribute reusable AI tasks, workflows, AI methods and report links in the
  AI Workflow Gallery — using the gallery's MCP tools when they are connected, or the gallery
  website in the browser when they are not.
  Use when the user asks to "check the gallery", "is there an existing workflow for",
  "find a prompt for", "how do we usually do this", "add this to the gallery", "write this up as a
  task", "capture this workflow", or asks for a standard approach before starting a piece of work.
  Do NOT use for general AI how-to questions with no gallery involvement, for producing the user's
  actual deliverable, or for other catalogs — answer directly or use the relevant tool instead.
license: MIT
metadata:
  author: AI Workflow Gallery
  version: "2.1"
---

# AI Workflow Gallery

## What this skill does

The gallery is this organisation's curated catalog of how we already work with AI. Before
improvising an approach, look here: someone has often solved it already, and the entry carries the
prompt, the caveats and the name of the person who wrote it.

> **Writes depend on the selected collection.** Public techniques and Curated tools are read-only.
> Anonymous authoring, when explicitly enabled, is confined to the shared, public and disposable
> Playground and uses a sandbox attribution actor, not a signed-in identity. Private Entra
> deployments retain their existing per-feature permissions and signed-in attribution. Always
> confirm the exact collection and show the full proposed change before saving; the gates in
> **Guardrails** below are not advice, and they are not skippable.

## When NOT to use this skill

Stop and do something else if:

- The user wants a **general answer**, not a catalog lookup — "how does RAG work" is not a gallery
  question. Answer it directly.
- The user wants you to **do the work**, not find how it is done. Use the gallery to find the
  approach, then leave this skill and get on with the task.
- The subject is **another catalog or repository** — a code repo, SharePoint, a Power BI workspace.
  This skill only knows this one gallery.
- The user is asking about the gallery **as a product** — how it is built, who runs it, why a
  feature is missing. Point them at the Feedback button rather than inventing an answer.

Using this skill where it does not belong costs the user a round trip and makes its results less
trusted when they do matter.

## Pick your route first

There are two ways to reach the gallery. Decide which one you have **before** you start, and tell
the user which you are using.

| Route | Use it when | How |
|---|---|---|
| **MCP tools** | Tools such as `search_tasks` and `get_workflow` are available to you | Call them directly. Details in `references/mcp-tools.md` |
| **Browser** | No such tools, but you can drive a browser | Use the website as the user would. Page and field map in `references/gallery-map.md` |

Prefer the MCP tools when you have them: they are faster and exact, and they do not depend on what
happens to be rendered on screen. Fall back to the browser only when the tools are absent.

The packaged Cowork integration is skills-only; it does not install a connector. Use the
authorized browser route unless actual connected MCP tools are available. API support for
Entra OAuth does not establish a particular cloud client's compatibility.

If you have neither, say so plainly and stop. Do not guess at the gallery's contents — an invented
entry is worse than no answer.

### Having the skill is not having the tools

This catches people out, so check before you promise anything. **Installing this skill does not
give you MCP tools.** The skill is instructions; the tools are a separate connection, and each of
the two is useless for proving the other:

- A skill folder on disk — for example `~/.scout/m-skills/ai-workflow-gallery` — means you know
  *how* to use the gallery. It does not mean you can reach it.
- A server entry in `~/.copilot/mcp-config.json` proves only that local **GitHub Copilot CLI**
  configuration exists. It proves neither reachability nor Scout compatibility.

The only reliable test is to look at the tools you actually have in this session. If
`search_tasks` is not among them, you do not have the MCP route, whatever the configuration files
say — use the browser route and tell the user the tools are not connected.

Native Scout, GitHub Copilot and Cowork compatibility requires documented support and live
verification of the exact anonymous transport or delegated registration/resource/permission flow. Do not invent a
native configuration, registration UI or startup behavior. For an existing enabled server,
the repository's maintained `integrations/reference-client` can initialize, list tools and
perform one bounded task search; that proves the reference run, not native tools in this
assistant session. See `references/mcp-tools.md` and the Tool integration guide.

For public MCP, explicitly select anonymous mode and one collection:
`--anonymous --collection-id public-techniques`, `--anonymous --collection-id curated-tools`,
or `--anonymous --collection-id playground`. No Entra discovery, token or dummy GUID is needed.
Public techniques and Curated tools expose only reads. Playground authoring is available only
when the owner confirms the separate write opt-in and reviewed SQL/storage protections; it is
shared, public and disposable. Use only the exact MCP endpoint confirmed by the deployment
owner. Published-read acceptance does not prove that Playground writes are active.
Never fall back from failed private authentication to anonymous access. In **both modes**,
every tool call requires `collectionId` exactly `public-techniques`, `curated-tools` or
`playground`; there is no default collection or global union. Public MCP writes require
`PublicPlaygroundWritesEnabled=true`; `EnableMcpWrites=true` alone never enables them. See the MCP reference for gates,
exact Origin policy and fixed per-worker limits; do not infer native compatibility.

Review proposed local configuration and dependency installation with the user before changes.
For private Entra, pause for human provider sign-in/consent. Conditional Access, admin-consent or admission denial
is a stop condition. Do not use direct JSON-RPC as an unverified setup workaround; use the
authorized browser fallback when native tools are absent.

### Where the gallery lives

Use the deployment URL explicitly supplied by the user or configured in the actual connected
tool. Name the environment; never substitute a historical endpoint. If MCP is disabled or its
readiness cannot be confirmed, stop MCP setup and use the authorized browser route. Installing
a skill does not enable MCP or prove that a connection is available.

In the browser you act inside the user's own signed-in session, so you see exactly what they see
and every change is recorded against their name. If a sign-in page appears, hand the browser back
to the user rather than trying to work around it.

### When the MCP tools suddenly fail

401 means absent/invalid/expired bearer authentication; use the registered client's supported
Entra sign-in. 403 means denied directory or delegated scope. 503 / `mcp_disabled` means stop
MCP setup; other 503 responses require owner review, not sign-in. HTML or redirects indicate a wrong endpoint or gateway; MCP never
redirects to login. See the Tool integration guide for the preregistered reference-client flow.

Never extract browser credentials, save tokens to client configuration or silently replay a
write. Delivery may be uncertain after network failure: inspect current content before deciding
whether another mutation is safe. Tools never present are a connection/registration issue;
installing the skill is not proof of an MCP connection.

## The four content types

Know which one you are looking for before you search. They hold genuinely different things, so a
miss in one is not a miss overall.

| Type | What it is | Tool | Page |
|---|---|---|---|
| **Task** | One reusable unit of work — usually a prompt, plus the tool it targets | `search_tasks` | `/?tab=tasks` |
| **Workflow** | An ordered sequence of steps, often chaining several tasks | `search_workflows` | `/?tab=workflows` |
| **AI Method** | A technique or pattern — *how* to approach a class of problem | `search_methods` | `/?tab=methods` |
| **Report link** | A pointer to an existing report or AI dashboard | `search_reports` | `/reports` |

## Workflow 1: Find out whether the work is already solved

Do this before answering any "how should I approach X" question.

1. Search **all three** of tasks, workflows and methods for the user's topic. Use a short noun
   phrase, not a sentence: `pipeline hygiene`, not `how do I clean up my pipeline`.
   - *With tools:* call `search_tasks`, `search_workflows` and `search_methods`.
   - *In the browser:* open `/?tab=tasks`, type into the **Search by keyword...** box — it filters
     as you type and takes no URL parameter — then repeat on the **Workflows** and **AI Methods**
     tabs.
2. Open the two or three most promising entries in full (`get_*`, or click through) and read them.
   Do not judge from a search summary.
3. Report back: what exists, how close each entry is to what the user actually needs, and what is
   genuinely missing. Name the entries and give their links.

If nothing fits, say so. A confident "there is nothing for this yet" is more useful than a
stretched match, and it is the signal that something should be written up.

## Workflow 2: Follow a workflow with the user

1. Find and open the workflow (Workflow 1).
2. Read the whole thing before starting. Summarise it in plain language: what it produces, what the
   user needs to have ready, and roughly how long it takes.
3. Walk the steps one at a time. After each, say what you did and what came back.
4. Stop at any step that needs the user's judgement rather than execution — an approval, a choice
   of account, anything with a business consequence.

Never silently skip a step you cannot complete. Name the step and say why.

## Workflow 3: Capture something that worked

The catalog is only as good as what people put back into it.

1. **Search first** (Workflow 1) — this is Gate 2, and a create is blocked until it has run. If a
   close entry exists, improve it (Workflow 4) rather than creating a near-duplicate.
2. Draft the entry:

   | Field | What to put |
   |---|---|
   | **Name** | A short, specific title someone would recognise in a list |
   | **Description** | What problem it solves and when to reach for it — not how it works |
   | **Task type** | Which kind of task it is |
   | **Tool / surface** | The tool it is meant for |
   | **Tags** | Comma-separated, for example `reporting, automation` |
   | **Reference link** | A link to source material, if there is one |
   | **Inline content** | The prompt or snippet itself — this is the valuable part |
   | **Data source** / **Data output** | What it expects in, what it produces |

3. **Show the user the complete draft before you save it** — every field, as you intend to submit
   it — and wait for their approval. This is Gate 1; there is no shortcut through it.
4. Create it (`create_task`, or **New task** on `/?tab=tasks`), then give them the link.

Use a workflow for an ordered sequence of steps, and an AI method for a technique rather than a
concrete prompt. Never invent content to fill a field: if you do not know the tool or the data
source, ask.

## Workflow 4: Improve an existing entry

Gate 3 governs this whole workflow.

1. Open the entry and read it in full.
2. Make the **smallest** change that fixes the problem. Do not rewrite someone else's entry in your
   own voice.
3. Show the user the exact before-and-after text of what you changed, then get approval and save.
4. If someone else edited it meanwhile, the gallery refuses the save and says so. Reload, re-apply
   your change, and tell the user it happened.

## Workflow 5: Find the right report or dashboard

Search the report links, open the best match, and tell the user what it will and will not answer.
In the browser this is `/reports`, with tabs for **Reports** and **AI Dashboards**.

## Guardrails

Gates 1-6 are **hard stops**, not preferences. Each one has a condition that must be satisfied
before you act. If you cannot satisfy it, you do not act — you say why and wait. "The user seemed
to want it" does not satisfy any of them.

### Gate 1 — Confirmation before every write

**Applies to:** `create_task`, `create_workflow`, `create_method`, `create_report`, every
`update_*`, and the equivalent **Save** in the browser.

Before you write anything, show the user the **complete draft — every field, verbatim, exactly as
it will be submitted** — and get explicit approval.

- Approval means the user says yes to *that* draft. Silence is not approval. "Sounds good" to your
  plan, earlier in the conversation, is not approval of the draft.
- If you change a single field after approval, show it again.
- **No approval, no write.** There is no version of this you can skip because the change is small
  or obviously correct.

### Gate 2 — No create without a completed search

A `create_*` is blocked until you have actually run the search across **tasks, workflows and
methods** (Workflow 1) *in this conversation*, and reported what it found.

"I didn't find one" is only a valid statement after that search has run. It is never valid from
memory, from earlier in the session, or from what you assume the catalog contains. If the search
failed or you could not run it, say so and do not create.

### Gate 3 — Editing someone else's entry

Before saving a change to an entry you did not just create:

1. Read it in full.
2. Show the user the **exact before-and-after text** of what you are changing — quoted, not
   summarised as "tidied up the description".
3. Get approval.

Make the smallest change that fixes the problem. **Never rewrite another author's entry wholesale
or into your own voice.** If it needs that much work, tell the user and let them decide.

### Gate 4 — Deletes

**Never invoke a delete the user did not ask for, in the same turn, in their own words, naming the
entry.**

Not "clean this up". Not "get rid of the duplicates". Not your own suggestion that they agreed to
in passing. If you think something should go, say so and stop — deleting is their decision to make
and their name on it.

Do not rely on the server to catch you. An unreferenced task can be deleted on the first call,
permanently; a referenced task returns an impact report before a `confirm: true` override.
Workflow, method and report MCP deletes always require `confirm: true` after the person has
seen and approved the impact. A method still referenced by a task or workflow remains
undeletable even after confirmation; its references must first be removed with approval.
These server gates never replace the user's explicit delete request above.

### Gate 5 — Ambiguity stops you

When the **scope**, the **environment** (production vs UAT) or **which entry** is meant is
unclear, ask **one** question and wait. Do not pick the likeliest default and proceed.

There is no default production environment in this personal deployment. If you
find yourself inferring which entry they meant from a partial name, that is the signal to ask.

### Gate 6 — Never fabricate

If the gallery has nothing, say it has nothing. A confident "there is nothing for this yet" is
useful and true; a plausible-sounding entry that does not exist is a real cost to whoever acts on
it. Never invent an entry, a link, an author or a field value to fill a gap.

### Working practice

Not gates, but how good work here looks:

- **Quote rather than paraphrase** when reporting what an entry says. The exact wording of a prompt
  matters.
- **Tell the user which route you are on** — MCP tools or browser — so they can judge your answer.
- **No file uploads.** Attachments have to be added by the user in the browser.

## Additional resources

- **`references/mcp-tools.md`** — tool names, arguments, and the ways the write tools differ from
  an ordinary REST API. Read this when the MCP tools are available.
- **`references/gallery-map.md`** — page and URL map, editor form fields, and expected site
  behaviour. Read this when working through the browser.

**If a reference file is missing**, you were installed from a single-file upload that did not carry
the `references/` folder. Say so, and carry on with reduced confidence — **never guess at the
gallery's page structure, URLs or form fields.** If a step depends on detail you do not have, stop
and tell the user which file is missing.

Both files can be downloaded individually from the gallery, under `/downloads/`, as
`ai-workflow-gallery-gallery-map.md` and `ai-workflow-gallery-mcp-tools.md`. Ask the user to save
them **next to this file, in a `references/` folder, renamed back to `gallery-map.md` and
`mcp-tools.md`** — the `ai-workflow-gallery-` prefix exists only to keep the download folder flat,
and the links above will not resolve unless it is dropped. The result should be:

```text
ai-workflow-gallery/SKILL.md
ai-workflow-gallery/references/gallery-map.md
ai-workflow-gallery/references/mcp-tools.md
```
