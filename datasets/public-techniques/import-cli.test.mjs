import test from 'node:test'
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const executable = fileURLToPath(new URL('./import/bin/Release/net10.0/PublicTechniques.Import.dll', import.meta.url))
function run(args, environment = {}) {
  const result = spawnSync('dotnet', [executable, ...args], {
    encoding: 'utf8',
    env: { ...process.env, PUBLIC_TECHNIQUES_TEST_CONNECTION: '', ...environment },
    timeout: 15_000,
  })
  assert.ifError(result.error)
  assert.equal(result.signal, null)
  return result
}

test('import help describes explicit isolated target and does not connect', () => {
  const result = run(['--help'])
  assert.equal(result.status, 0)
  assert.match(result.stdout, /--target-database/)
  assert.match(result.stdout, /never creates a schema, user, role/)
})

test('missing, duplicate, unknown and ambiguous mode arguments are visibly rejected', () => {
  for (const args of [[], ['--apply', '--apply'], ['--unknown'], ['--catalog'], ['--catalog', '--dry-run']]) {
    const result = run(args)
    assert.equal(result.status, 1)
    assert.match(result.stderr, /Import rejected:/)
  }
})

const required = [
  '--catalog', 'unused.json', '--collection', 'public-techniques',
  '--connection-env', 'PUBLIC_TECHNIQUES_TEST_CONNECTION', '--target-database', 'isolated-test',
  '--actor-id', 'bfb7a22e-9e71-455b-9a9d-bd29b9dc1949',
]
test('an explicit no-write or apply mode is mandatory', () => {
  for (const suffix of [[], ['--apply', '--dry-run']]) {
    const result = run([...required, ...suffix])
    assert.equal(result.status, 1)
    assert.match(result.stderr, /Choose exactly one/)
  }
})

test('curated-tools collection cannot be populated by this public corpus importer', () => {
  const args = [...required, '--dry-run']
  args[3] = 'curated-tools'
  const result = run(args)
  assert.equal(result.status, 1)
  assert.match(result.stderr, /imports only the public-techniques collection/)
})

test('empty credential boundary is rejected without logging secrets or connecting', () => {
  const result = run([...required, '--dry-run'])
  assert.equal(result.status, 1)
  assert.match(result.stderr, /environment variable is empty/)
})
