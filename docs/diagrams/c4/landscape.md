# System landscape

[Architecture tour](../../architecture.md) | [Next: system context](context.md)

**Learn:** a landscape places systems beside one another. It does not expose the
gallery's internal database or frontend; those belong at the next zoom levels.
The public gallery and a private installation have the same product purpose but
separate deployment-owned data, configuration and identities.

```mermaid
flowchart LR
    visitor["Public visitor<br/>Person: discovers reusable techniques"]
    member["Private member / administrator<br/>Person: reads and maintains authorized content"]
    owner["Content owner / deployment operator<br/>Person: reviews imports and operates installations"]
    assistant["Assistant host or reference client<br/>External software system: consumes MCP"]
    public["Public gallery<br/>Software system: anonymous scoped catalog"]
    private["Private gallery<br/>Software system: delegated authenticated catalog"]
    entra["Microsoft Entra ID<br/>External software system: identity and signing metadata"]
    inference["Microsoft Foundry / Azure OpenAI<br/>External software system: optional model inference"]
    github["GitHub<br/>External software system: source, CI and release packages"]
    destinations["Referenced tools / report providers<br/>External software systems: run their own services"]
    cowork["Organization-approved skill host<br/>External software system: loads optional package"]
    visitor -->|"Browse via HTTPS"| public
    member -->|"Browse / edit via HTTPS"| private
    member -->|"Provider-managed sign-in"| entra
    assistant -->|"Scoped MCP over HTTPS; deployment opt-in"| public
    assistant -->|"Scoped MCP + delegated bearer; opt-in"| private
    private -->|"Validate issuer / signing keys"| entra
    public -.->|"Budgeted proposal inference; opt-in"| inference
    private -.->|"Proposal inference; opt-in"| inference
    owner -->|"Reviewed offline import / Azure operations"| public
    owner -->|"Authorized maintenance / Azure operations"| private
    owner -->|"Maintains source and release assets"| github
    github -.->|"Organization-approved skills ZIP; no access grant"| cowork
    visitor -->|"Follow catalog links"| destinations
    member -->|"Follow report / tool links"| destinations
```

There is deliberately **no public-to-private gallery arrow**. A public collection
does not read another deployment's private SQL. A downloaded skill package is
instructions, not an identity, native connector, access grant or workflow runner.
No installation into an organization's skill host is implied by publication.

| Neighbor | Relationship | Responsibility that stays outside the gallery |
|---|---|---|
| Entra | Private human sign-in, access-token issuance, API signing metadata | Account lifecycle, guest admission, consent and Conditional Access |
| Foundry / Azure OpenAI | Optional chat-completion inference | Model hosting and inference; not execution of catalog workflows |
| GitHub | Source, automated checks and downloadable packages | Repository/account administration; not runtime catalog authorization |
| Referenced providers | User opens a link or supported report view | Provider sign-in, licensing and authorization; a catalog link grants none |
| Skill host / assistant | Imports instructions or explicitly configured MCP connection | Organizational clearance, tool approval and native client compatibility |

**Evidence:** [product contract](../../../README.md),
[client integration contract](../../connect-production-mcp-server.md),
[package privacy](../../package-privacy.md),
[integration manifest](../../../integrations/cowork-plugin/manifest.json).
