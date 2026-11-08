# Integration packages — changelog

Changes to the artifacts under `integrations/`: the `ai-workflow-gallery` skill, its references,
and the Cowork plugin manifest.

These are tracked separately from the application because they **leave the repository**. Once a
user has uploaded `SKILL.md` into Cowork or unzipped the skill into Scout, their copy does not
update when this repo does. A change that looks cosmetic here can alter how an assistant behaves in
someone else's session weeks later, with no obvious way for them to tell what changed — so every
behavioural change gets a version bump and an entry.

Versions refer to `metadata.version` in
[`skills/ai-workflow-gallery/SKILL.md`](skills/ai-workflow-gallery/SKILL.md).

## 2.1 — 2026-11-05

Aligned shipped skill/reference instructions with the manual connection guides. Local config
is not reachability proof. Removed unverified Scout registration/startup claims and the direct
JSON-RPC setup stopgap. Existing enabled deployments use the maintained delegated reference
flow for bounded read verification; native-client compatibility remains separate. Disabled MCP
deployments return `503` / `mcp_disabled`; readiness is verified for the selected deployment,
not inferred from an environment name. Local setup needs review before changes
and human provider sign-in/consent; policy/admission denials stop setup. Package structure and
download URLs are unchanged; no connector, permission or live integration is added.

## 2.0 — 2026-11-04

Replaced captured browser credentials and transparent replay helpers with preregistered
delegated Entra OAuth. The reference client uses maintained MSAL/MCP SDKs and in-memory
tokens. Client compatibility and provider consent are explicit gates, not inferred from a URL.
Deployment selection is explicit; legacy enterprise hosts are not defaults. The packaged
skill remains dual-mode and skills-only for Cowork. Instructions contain no shared credential.

## 1.1 — 2026-09-21 (historical, superseded by 2.0)

**Fixed: a single-file install had two dangling references.** `SKILL.md` points to
`references/gallery-map.md` and `references/mcp-tools.md`, but those only ever shipped inside the
two archives. Anyone following Cowork's "Option A" (upload `SKILL.md`) installed a skill whose own
"Additional resources" links resolved to 404, and whose browser route ran without the page and
field map it depends on. Both references are now published individually under `/downloads/`, the
guides list them, and the skill explains what to do when a reference is absent: say so and continue
with reduced confidence, never guess at the site's structure.

**Fixed: the Cowork guide and the skill contradicted each other on MCP.** The guide argues at the
bottom that no MCP connector is possible for Cowork; the skill nonetheless routed Cowork readers to
`references/mcp-tools.md` and offered MCP session-recovery steps involving
`get-gallery-cookie.mjs` and `mcp-proxy.mjs`, none of which that surface can use. The skill now
states plainly that Cowork has no MCP route and is not meant to, and scopes the recovery section to
the surfaces that do. The browser route is presented as complete rather than degraded.

**Added: write operations are gated rather than advised.** The skill creates and edits entries in a
shared catalog, attributed to the signed-in user by name, and on the browser route it cannot undo
them. The previous "Rules" section was sound advice with no gate in it. It is replaced by six
numbered hard stops:

1. **Confirmation** — show the complete draft, every field verbatim, and get explicit approval
   before any write. Re-approval is required if any field changes afterwards.
2. **Duplicate search** — a create is blocked until the search across tasks, workflows and methods
   has actually run in the conversation and been reported. "I didn't find one" is not valid from
   memory.
3. **Edits to others' entries** — quote the exact before-and-after and get approval; never rewrite
   another author's entry wholesale.
4. **Deletes** — never invoke one the user did not request in the same turn, in their own words,
   naming the entry. Called out explicitly because `delete_task` has no server-side confirmation.
5. **Ambiguity** — when scope, environment or the target entry is unclear, ask one question and
   wait instead of choosing a default.
6. **No fabrication** — if the gallery has nothing, say so.

**Added: routing hygiene.** A "Do NOT use …" clause in the frontmatter description and a "When NOT
to use this skill" section, so the skill declines work that is not a gallery lookup instead of
being invoked for general questions. An attribution warning now sits near the top, where it is read
before the first write rather than after it.

**Added: telling a real connection from a configured one.** Installing the skill does not provide
MCP tools, and an entry in `~/.copilot/mcp-config.json` proves only that the GitHub Copilot CLI can
connect — not that Scout has loaded anything. The skill now says to check the session's actual tool
list, and `references/mcp-tools.md` carries the exact Scout remote-HTTP registration values, the
equivalent JSON, and the reminder that Scout loads MCP tools at session startup, so a registration
without a restart shows nothing.

**Corrected: how deletes are actually gated.** The 1.0 reference said `delete_workflow`,
`delete_method` and `delete_report` refuse the first call while `delete_task` "has no such gate".
That is wrong in both directions. All four refuse only when something still references the entry,
returning the list of what would break; an entry nothing references is deleted on the first call,
permanently. An agent told the refusal is a confirmation prompt will treat a 409 impact report as a
fault, and one told `delete_task` is ungated may over-warn on the safe case and under-warn on the
dangerous one.

**Changed: environment handling is explicit.** Production is the default and UAT is used only on
request; setup commands name the environment (`--env prod` / `--env uat`) rather than relying on a
default that has already changed once.

## 1.0 — 2026-09-20

Initial release: the dual-mode skill (MCP tools where available, browser otherwise), its two
references, and the Cowork plugin manifest. Packaged by
`frontend/scripts/build-integration-downloads.mjs` into a skill ZIP, a bare `SKILL.md` and a Cowork
plugin ZIP.
