import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { integrationGuides } from '../../src/pages/Integrations/integrationGuides'

describe('published integration instructions', () => {
  it('does not publish captured-credential or legacy enterprise endpoint setup', () => {
    const guides = JSON.stringify(integrationGuides.map((guide) => guide.sections('https://gallery.example')))
    expect(guides).not.toMatch(/StaticWebAppsAuthCookie|get-gallery-cookie|mcp-proxy\.mjs|nice-cliff|icy-stone/)
    expect(guides).toContain('preregistered')
    expect(guides).toContain('uncertain')
  })

  it('keeps downloadable skill sources free of obsolete credential setup', async () => {
    for (const file of ['SKILL.md', 'references/mcp-tools.md', 'references/gallery-map.md']) {
      const source = await readFile(resolve('..', 'integrations', 'skills', 'ai-workflow-gallery', file), 'utf8')
      expect(source).not.toMatch(/StaticWebAppsAuthCookie|get-gallery-cookie|mcp-proxy\.mjs|nice-cliff|icy-stone/)
    }
  })

  it('ships consistent skill readiness and removes unsupported native setup claims', async () => {
    const skill = await readFile(resolve('..', 'integrations', 'skills', 'ai-workflow-gallery', 'SKILL.md'), 'utf8')
    const reference = await readFile(resolve('..', 'integrations', 'skills', 'ai-workflow-gallery', 'references', 'mcp-tools.md'), 'utf8')
    expect(skill).toContain('version: "2.1"')
    for (const source of [skill, reference]) {
      expect(source).toContain('mcp_disabled')
      expect(source).toContain('browser')
      expect(source).toContain('reference-client')
      expect(source).not.toMatch(/proves the \*\*GitHub Copilot CLI\*\* can reach|Add MCP Server|at session startup|Treat that as a stopgap/)
    }
    expect(skill).toContain('Review proposed local configuration')
    expect(skill).toContain('pause for human provider sign-in/consent')
  })

  it('keeps every self-install instruction manual, scoped and consent-bound', () => {
    for (const guide of integrationGuides) {
      const sections = guide.sections('https://gallery.example')
      const instruction = sections.find((section) => section.id === 'assistant')!.body
      expect(instruction).toContain(`https://gallery.example${guide.path}`)
      expect(instruction).toContain('Wait for my approval')
      expect(instruction).toContain('Do not deploy/enable MCP')
      expect(instruction).toContain('Pause for me to complete provider sign-in/consent')
      expect(instruction).toContain('Reference-client success is not proof of native tools')
      expect(instruction).toContain('503 mcp_disabled')
      expect(sections[0].body).toContain('MCP availability is deployment-specific')
      expect(guide.statusDetail).toMatch(/does not establish|do not establish|not proof/)
      expect(sections[0].body).not.toMatch(/personal UAT|private UAT|mcp_disabled/)
    }
  })

  it('uses the real reference-client flags and checked Windows commands in every guide', () => {
    for (const guide of integrationGuides) {
      const body = guide.sections('https://gallery.example').find((section) => section.id === 'verify')!.body
      expect(body).toContain('npm.cmd --prefix .\\integrations\\reference-client ci')
      expect(body).toContain('node .\\integrations\\reference-client\\client.mjs --endpoint $endpoint --tenant $tenantId --client-id $publicClientId --api-client-id $apiClientId --collection-id $collectionId')
      expect(body).toContain('node .\\integrations\\reference-client\\client.mjs --endpoint $endpoint --anonymous --collection-id $collectionId')
      expect(body).toContain("if ($LASTEXITCODE -ne 0) { throw 'Reference-client dependency installation failed.' }")
      expect(body).toContain("if ($LASTEXITCODE -ne 0) { throw 'Reference-client verification failed; stop and inspect the error.' }")
      expect(body).toContain('page: 1, pageSize: 1')
      expect(body).toContain('taskSearchSucceeded: true')
    }
  })

  it('keeps repository copy/paste blocks aligned with the in-app reference flow', async () => {
    for (const file of ['docs/connect-production-mcp-server.md', 'integrations/reference-client/README.md']) {
      const source = await readFile(resolve('..', file), 'utf8')
      expect(source).toContain('npm.cmd --prefix .\\integrations\\reference-client ci')
      expect(source).toContain('node .\\integrations\\reference-client\\client.mjs --endpoint $endpoint --tenant $tenantId --client-id $publicClientId --api-client-id $apiClientId --collection-id $collectionId')
      expect(source).toContain('node .\\integrations\\reference-client\\client.mjs --endpoint $endpoint --anonymous --collection-id $collectionId')
      expect(source).toContain('mcp_disabled')
      expect(source).not.toMatch(/nice-cliff|icy-stone|kind-tree|MngEnvMCAP/)
    }
  })

  it('distinguishes explicit public reads from private Entra without claiming live/native support', () => {
    for (const guide of integrationGuides) {
      const sections = guide.sections('https://gallery.example')
      const oauth = sections.find((section) => section.id === 'oauth')!.body
      const instruction = sections.find((section) => section.id === 'assistant')!.body
      expect(oauth).toContain('both modes')
      expect(oauth).toContain('No Entra discovery, token or dummy GUID')
      expect(oauth).toContain('Never fall back to anonymous')
      expect(oauth).toContain('PublicDemoEnabled=true')
      expect(oauth).toContain('EnableMcpServer=true')
      expect(oauth).toContain('EnablePublicMcp=true')
      expect(oauth).toContain('PublicPlaygroundWritesEnabled=true')
      expect(oauth).toContain('EnableMcpWrites=true alone never enables')
      expect(oauth).toContain('PublicMcpAllowedOrigins')
      for (const bound of ['64 KiB', '512 KiB', '25', '20 seconds', '2 concurrent', '60 admissions', 'per worker', 'Retry-After']) {
        expect(oauth).toContain(bound)
      }
      expect(oauth).toContain('no silent truncation')
      expect(oauth).toContain('Not a global guarantee')
      expect(instruction).toContain('Collection: [public-techniques OR curated-tools OR playground]')
      expect(instruction).toContain('Never fall back to anonymous')
      expect(sections[0].body).toContain('not proof of live MCP access')
    }
  })

  it('publishes the same public policy and explicit collection contract in the MCP reference', async () => {
    for (const file of [
      'docs/connect-production-mcp-server.md',
      'integrations/skills/ai-workflow-gallery/references/mcp-tools.md',
    ]) {
      const source = await readFile(resolve('..', file), 'utf8')
      for (const text of ['PublicDemoEnabled=true', 'EnableMcpServer=true', 'EnablePublicMcp=true',
        'PublicPlaygroundWritesEnabled=true', 'EnableMcpWrites', 'PublicMcpAllowedOrigins',
        '64 KiB', '512 KiB', 'Retry-After',
        'collectionId', 'public-techniques', 'curated-tools', 'playground']) {
        expect(source).toContain(text)
      }
      expect(source).toMatch(/per worker/)
      expect(source).toMatch(/(?:https:\/\/<[^>]+>|<chosen-origin>)\/api\/mcp/)
      expect(source).not.toMatch(/aigallery\.liberatetheinnerchild\.net|kind-tree|nice-cliff|icy-stone/)
    }
  })

  it('keeps the PowerShell smoke tool explicitly scoped, auth-exclusive and write-confirmed', async () => {
    const source = await readFile(resolve('..', 'scripts/test-mcp-server.ps1'), 'utf8')
    expect(source).toContain("DefaultParameterSetName = 'Entra'")
    expect(source).toContain("ParameterSetName = 'Anonymous'")
    expect(source).toContain("SupportsShouldProcess = $true, ConfirmImpact = 'High'")
    expect(source).toContain("[ValidateSet('public-techniques', 'curated-tools', 'playground')]")
    expect(source).toContain('$Params.arguments.collectionId = $CollectionId')
    expect(source).toContain("Write verification was not approved; no requests were sent.")
    expect(source).toContain('if (-not $Anonymous)')
  })
})
