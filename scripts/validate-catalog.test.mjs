import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { validateRegistry } from './validate-catalog.mjs'

const registry = JSON.parse(readFileSync(new URL('../catalog/registry.json', import.meta.url), 'utf8'))
test('reviewed registry has separate execution, context, authoring, governance and access roles', () => {
  validateRegistry(registry)
  assert.equal(registry.targets.find((target) => target.id === 'microsoft.frontier').role, 'access-program')
  assert.equal(registry.targets.find((target) => target.id === 'microsoft.frontier').profiles.length, 0)
  assert.notDeepEqual(
    registry.targets.find((target) => target.id === 'github.copilot.cli').profiles,
    registry.targets.find((target) => target.id === 'github.copilot.app').profiles,
  )
})
for (const [name, mutate] of [
  ['duplicate target ID', (data) => data.targets.push(data.targets[0])],
  ['unknown kind', (data) => { data.targets[0].profiles[0].kind = 'invented' }],
  ['unknown format', (data) => { data.targets[0].profiles[0].formats = ['invented'] }],
  ['non-HTTPS source', (data) => { data.targets[0].profiles[0].sourceUrl = 'http://example.org' }],
  ['missing evidence', (data) => { data.targets[0].profiles[0].note = '' }],
  ['invalid date', (data) => { data.targets[0].profiles[0].reviewedOn = '2026-02-31' }],
  ['access program as runtime', (data) => { data.targets.find((target) => target.id === 'microsoft.frontier').profiles = data.targets[0].profiles }],
]) {
  test(`rejects ${name}`, () => {
    const changed = structuredClone(registry)
    mutate(changed)
    assert.throws(() => validateRegistry(changed), /Catalog registry/)
  })
}
