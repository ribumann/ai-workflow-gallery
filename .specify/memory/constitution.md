<!--
Sync Impact Report
- Version change: 1.0.1 → 2.0.0 (MAJOR: explicitly admitted organizational tenants replace
  the Microsoft-only access boundary for this gallery)
- Modified principles: II, III — admitted users share one gallery; authentication is unified
  across browser, API and MCP while existing feature-specific permissions remain authoritative
- Added sections: none
- Removed sections: none
- Templates requiring updates: .specify/templates/plan-template.md (✅ compatible, no changes needed),
  .specify/templates/spec-template.md (✅ compatible), .specify/templates/tasks-template.md (✅ compatible)
- Follow-up: tenant and organization isolation require a separate specification; admission alone
  does not imply isolation.
-->

# AI Workflow Gallery Constitution

## Core Principles

### I. Workflow-First, Not Just a Catalog
The core unit of value is the **workflow**: a defined sequence linking two or more tasks
(agents, skills, plugins, prompts, MCP servers/tools) across M365 Copilot, GitHub Copilot, Scout,
and Cowork to solve a specific problem and drive a specific outcome. Tasks MAY be browsed and
managed individually, but every feature MUST treat the workflow — not the task list — as the
primary object of value. A workflow MUST record: the problem/outcome it addresses, the ordered
tasks it links, and which tool/surface each step runs in.

### II. Collaborative & Editable by Design
This is not a read-only reference catalog. Every task and workflow MUST be creatable, editable,
and versionable in place by authorized users of explicitly admitted tenants, with clear ownership
and change history. Contributions from onboarded users MUST be supported without a code change or developer
involvement. Rationale: the gallery's value compounds only if the community that uses it can also
shape it.

### III. Explicit Tenant Admission and Verified Identity (NON-NEGOTIABLE)
The application MUST authenticate content access through Microsoft Entra ID and authorize only
explicitly admitted organizational tenants. Browser, API and MCP access MUST use the same validated
identity and admission policy; missing identity or invalid configuration MUST fail closed.
Email domains, client-supplied identity headers and display names MUST NOT establish identity,
tenant admission or ownership. User identity MUST be tenant-qualified using validated `tid` and `oid`.

The current deployment intentionally provides ONE SHARED GALLERY to admitted tenants. It MUST NOT
claim tenant or organization data isolation. Microsoft enterprise-tenant admission MUST have an
explicit deployment switch, OFF by default; OFF takes precedence over general allowlist membership.
Changes to that policy MUST apply to subsequent requests, including requests with unexpired tokens.
Admission does not override Entra consent requirements or Conditional Access.

Existing feature-specific edit/delete and owner/admin rules MUST be preserved consistently in
browser and MCP operations. This amendment does not introduce a reader-only default, a new
contributor hierarchy, or blanket owner-only deletion. Delegated client scopes MUST NOT grant
permissions beyond the acting user's existing feature permissions. Deployment identities MUST NOT
impersonate human contributors. True tenant isolation and in-tenant organization configuration
require a separate specification and approved implementation.

### IV. Fluent 2 & Native Conventions
All user interface work MUST use Fluent UI React v9 (Fluent 2) components, tokens, and layout
patterns as the single design system — no parallel or forked component library, no ad hoc CSS that
bypasses Fluent tokens. Rationale: consistency across the gallery and reduced UI maintenance burden;
the `impeccable` skill governs the craft/quality bar applied on top of Fluent 2, it does not replace it.

### V. Spec-Driven, Incrementally Delivered
Every feature MUST flow through the Spec Kit lifecycle (`/speckit-constitution` →
`/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement`)
before code is written. The product MUST ship as a minimal, functional MVP first; scope expansions
are added as new specs, not by silently growing an in-flight feature. Every planned unit of work
MUST be tracked as a GitHub issue linked to the project board before implementation begins, so
progress and scope stay visible to collaborators.

### VI. Infrastructure as Code & Safe Automation
All Azure infrastructure MUST be defined as code (Bicep or Terraform) and reviewed like application
code — no manual portal changes to environments that infra-as-code manages. CI/CD MUST be
implemented with GitHub Actions from the start of the project, covering build, test, lint, and
deploy, but workflows MUST be created in a disabled/manual-trigger state until the team explicitly
enables automatic runs. Secrets and credentials MUST never be committed to the repository; they are
sourced from GitHub Actions secrets or Azure Key Vault at runtime.

## Technology Stack Standards

- Frontend: React with TypeScript, using Fluent UI React v9 (Fluent 2) as the component and design
  token system.
- Backend: .NET / C# API.
- Hosting/data platform: Azure-native services; the specific hosting topology (e.g., Static Web Apps,
  Container Apps, App Service) and data store are decided during the architecture/plan phase, not
  hard-coded here, but MUST remain within Azure and MUST support the explicit Entra tenant-admission model in
  Principle III.
- All environment configuration (connection strings, tenant IDs, feature flags) MUST be externalized
  from source code.

## Development Workflow

- Feature work follows the Spec Kit lifecycle end-to-end; skipping `/speckit-specify` or
  `/speckit-plan` for non-trivial features is not permitted.
- Approved specs and plans are broken into small, independently shippable tasks and synced to
  GitHub Issues / the GitHub Project so execution status is visible without reading chat history.
- Changes land through pull requests; PRs MUST reference the GitHub issue(s) they close and MUST
  note any constitution or spec deviations explicitly.
- Releases are iterative: prefer shipping a smaller vertical slice sooner over batching a large
  release, once the MVP baseline defined by the first spec is complete.

## Governance

This constitution supersedes ad hoc practice for this repository. Amendments are made by editing this
file via pull request, incrementing the version per semantic versioning (MAJOR: incompatible
principle removal/redefinition; MINOR: new principle or materially expanded guidance; PATCH:
clarifications/wording), and updating the Sync Impact Report at the top of the file. Every plan
(`/speckit-plan`) MUST include a Constitution Check confirming compliance or documenting a justified,
reviewed deviation. Complexity or deviation from these principles MUST be justified in the relevant
spec or plan document, not silently absorbed into implementation.

**Version**: 2.0.0 | **Ratified**: 2026-08-26 | **Last Amended**: 2026-11-04
