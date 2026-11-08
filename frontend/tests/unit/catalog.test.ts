import { describe, expect, it } from 'vitest'
import { catalog, formatsFor, targetsFor, validateClassification } from '../../src/services/catalog'

describe('shared catalog definitions', () => {
  it('keeps access programs, workspace context and governance out of artifact choices', () => {
    const choices = targetsFor().map((target) => target.id)
    expect(choices).not.toContain('microsoft.frontier')
    expect(choices).not.toContain('microsoft.pages')
    expect(choices).not.toContain('microsoft.agent365')
    expect(catalog.targets.find((target) => target.id === 'microsoft.frontier')?.role).toBe('access-program')
  })

  it('intersects formats across explicit targets, without inferring portability', () => {
    expect(formatsFor('plugin', ['microsoft.copilot.cowork', 'anthropic.claude-code.cli'])).toEqual([])
    expect(validateClassification('plugin', ['anthropic.claude-code.cli'], 'microsoft.plugin')).toMatch(/format/)
    expect(validateClassification('prompt', ['github.copilot.cli', 'microsoft.copilot.chat'], 'text.plain')).toBeNull()
  })

  it.each([
    ['unknown-kind', ['microsoft.copilot.chat'], null],
    ['prompt', [], null],
    ['prompt', ['legacy-ambiguous-target'], null],
    ['prompt', ['microsoft.frontier'], null],
    ['prompt', ['microsoft.copilot.chat', 'microsoft.copilot.chat'], null],
    ['mcp-tool', ['github.copilot.cli'], 'mcp.server-json'],
  ] as const)('rejects unknown or incompatible classification %s %j', (kind, targets, format) => {
    expect(validateClassification(kind, [...targets], format)).not.toBeNull()
  })

  it('does not extrapolate CLI hooks to the app', () => {
    expect(targetsFor('hook').some((target) => target.id === 'github.copilot.cli')).toBe(true)
    expect(targetsFor('hook').some((target) => target.id === 'github.copilot.app')).toBe(false)
  })
})
