import { mkdirSync, mkdtempSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'

const root = dirname(dirname(fileURLToPath(import.meta.url)))
const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm'
const python = process.platform === 'win32' ? 'python' : 'python3'
const project = join('api', 'tests', 'WorkflowGallery.Api.Tests.csproj')

function run(command, args, cwd = root) {
  console.log(`> ${command} ${args.join(' ')}`)
  const executable = command === 'npm.cmd' ? (process.env.ComSpec ?? 'cmd.exe') : command
  const commandArgs = command === 'npm.cmd' ? ['/d', '/c', command, ...args] : args
  const result = spawnSync(executable, commandArgs, { cwd, stdio: 'inherit' })
  if (result.error) throw result.error
  if (result.status !== 0) {
    const error = new Error(`Validation command failed${result.signal ? ` (${result.signal})` : ''}.`)
    error.exitCode = result.status ?? 1
    throw error
  }
}

const suites = {
  catalog() {
    run('node', [join('scripts', 'validate-catalog.mjs')])
    run('node', ['--test', join('scripts', 'validate-catalog.test.mjs')])
  },
  dataset() {
    run('node', [join('datasets', 'public-techniques', 'build.mjs'), '--check'])
    run('node', ['--test', join('datasets', 'public-techniques', 'catalog.test.mjs')])
    run('dotnet', ['build', join('datasets', 'public-techniques', 'import', 'PublicTechniques.Import.csproj'), '--configuration', 'Release'])
    run('node', ['--test', join('datasets', 'public-techniques', 'import-cli.test.mjs')])
  },
  api() {
    run('node', ['--experimental-test-module-mocks', '--test', join('scripts', 'configure-entra.test.mjs')])
    run('node', ['--test', join('scripts', 'playground-reset-safety.test.mjs')])
    run('dotnet', ['--version'])
    run('dotnet', ['--list-runtimes'])
    run(python, ['-B', join('scripts', 'test_api_test_results.py')])
    run('dotnet', ['restore', project])
    run('dotnet', ['build', project, '--no-restore', '--configuration', 'Release'])
    const resultsRoot = join(root, 'TestResults')
    mkdirSync(resultsRoot, { recursive: true })
    const results = mkdtempSync(join(resultsRoot, 'validation-'))
    run('dotnet', ['test', project, '--no-build', '--configuration', 'Release',
      '--logger', 'trx', '--results-directory', results])
    run(python, ['-B', join('scripts', 'assert-api-test-results.py'), results])
  },
  frontend() {
    const cwd = join(root, 'frontend')
    run('node', ['--test', join('scripts', 'branding.test.mjs')], cwd)
    run(npm, ['run', 'lint'], cwd)
    run(npm, ['test', '--', '--maxWorkers=2', '--testTimeout=15000'], cwd)
    run(npm, ['run', 'build'], cwd)
  },
  browser() {
    run(npm, ['run', 'test:e2e'], join(root, 'frontend'))
    run(npm, ['exec', 'playwright', 'test', '--', '--config=playwright.public.config.ts'], join(root, 'frontend'))
  },
  reference() {
    run(npm, ['test'], join(root, 'integrations', 'reference-client'))
  },
}

try {
  const selected = process.argv.slice(2)
  if (selected.length > 1 || (selected[0] && selected[0] !== 'all' && !Object.hasOwn(suites, selected[0]))) {
    throw new Error('Usage: node scripts/validate.mjs [all|catalog|dataset|api|frontend|browser|reference]')
  }
  const names = !selected[0] || selected[0] === 'all' ? Object.keys(suites) : selected
  console.log(`Validation scope: ${names.join(', ')}. These checks do not establish live delegated UAT acceptance.`)
  for (const name of names) suites[name]()
  console.log(`Validation passed for: ${names.join(', ')}.`)
} catch (error) {
  console.error(`Validation FAILED: ${error.message}`)
  process.exitCode = error.exitCode ?? 1
}
