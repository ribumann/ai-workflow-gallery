import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { buildCatalog, collectionId, corpusPath, counts, datasetId, stableId } from './build.mjs'

const catalog = buildCatalog()
const tasks = new Map(catalog.tasks.map(task => [task.id, task]))
const methods = new Map(catalog.methods.map(method => [method.id, method]))
const workflows = new Map(catalog.workflows.map(workflow => [workflow.id, workflow]))
const sources = new Map(catalog.sources.map(source => [source.id, source]))
const normalize = text => text.toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim()

test('checked-in deterministic JSON is reproducible and meets exact editorial counts', () => {
  assert.equal(readFileSync(corpusPath, 'utf8'), `${JSON.stringify(catalog, null, 2)}\n`)
  for (const [kind, expected] of Object.entries(counts)) assert.equal(catalog[kind].length, expected, kind)
  assert.equal(catalog.collectionId, collectionId)
  assert.equal(catalog.datasetId, datasetId)
  assert.equal(stableId('task', 'claim-ledger'), tasks.values().next().value.id)
})

test('stable identifiers, names, actual procedures and semantic jobs are unique', () => {
  const all = [...catalog.tasks, ...catalog.methods, ...catalog.workflows]
  assert.equal(new Set(all.map(item => item.id)).size, all.length)
  assert.equal(new Set(all.map(item => item.fixtureId)).size, all.length)
  for (const key of ['name', 'technique', 'dataOutput']) {
    // Output labels can overlap legitimately; duplicate title/procedure cannot.
    const values = catalog.tasks.map(task => normalize(task[key]))
    if (key !== 'dataOutput') assert.equal(new Set(values).size, values.length, `Duplicate ${key}`)
  }
  const concepts = catalog.tasks.map(task => normalize(`${task.dataSource} ${task.technique} ${task.dataOutput}`))
  assert.equal(new Set(concepts).size, concepts.length)
})

test('source provenance is public, actually reviewed and retained offline', () => {
  const hosts = new Set(['learn.microsoft.com', 'support.microsoft.com', 'docs.github.com',
    'learn.chatgpt.com', 'code.claude.com', 'agentskills.io', 'modelcontextprotocol.io'])
  assert.equal(new Set(catalog.sources.map(source => source.url)).size, counts.sources)
  for (const source of catalog.sources) {
    const url = new URL(source.url)
    assert.equal(url.protocol, 'https:')
    assert.ok(hosts.has(url.hostname), source.url)
    assert.equal(url.username + url.password, '')
    assert.equal(source.verification, 'fetched-and-reviewed')
    assert.equal(source.checkedAt, catalog.checkedAt)
    assert.match(source.checkedAt, /^\d{4}-\d{2}-\d{2}$/)
    assert.ok(source.title.length > 8)
    assert.ok(source.reviewSummary.length > 60)
    assert.match(source.attribution, /no source text or code redistributed/i)
  }
  for (const item of [...catalog.tasks, ...catalog.methods, ...catalog.workflows]) {
    assert.ok(item.sourceIds.length > 0, item.fixtureId)
    for (const sourceId of item.sourceIds) assert.ok(sources.has(sourceId), sourceId)
  }
  for (const task of catalog.tasks) {
    assert.ok(task.sourceIds.some(id => sources.get(id).url === task.referenceLink))
  }
})

test('each reusable task includes original applicability, inputs, steps, output and review', () => {
  for (const task of catalog.tasks) {
    assert.equal(task.collectionId, collectionId)
    assert.ok(task.applicability.length > 20, task.fixtureId)
    assert.ok(task.dataSource.length > 12, task.fixtureId)
    assert.ok(task.steps.length >= 3, task.fixtureId)
    assert.ok(task.dataOutput.length > 10, task.fixtureId)
    assert.ok(task.reviewCriteria[0].length > 20, task.fixtureId)
    assert.ok(task.inlineContent.length > 100, task.fixtureId)
    assert.ok(methods.has(task.methodId), task.fixtureId)
    assert.ok(task.targetIds.length > 0)
    assert.equal(new Set(task.targetIds).size, task.targetIds.length)
    assert.equal(task.artifact.kind, task.taskType)
    assert.equal(task.artifact.format, task.format)
    assert.equal(task.compatibility.galleryConnection, 'not-verified')
    assert.equal(task.compatibility.installation, 'not-installed')
    assert.equal(task.attachedFile, undefined)
    if (task.format === null) assert.equal(task.artifact.form, 'reference-only')
  }
})

test('workflows have distinct ordered useful sequences and same-collection references', () => {
  const sequences = new Set()
  for (const workflow of catalog.workflows) {
    assert.equal(workflow.collectionId, collectionId)
    assert.ok(methods.has(workflow.methodId))
    assert.ok(workflow.steps.length >= 2)
    assert.equal(new Set(workflow.steps.map(step => step.taskId)).size, workflow.steps.length)
    const signature = workflow.steps.map(step => step.taskId).join('|')
    assert.ok(!sequences.has(signature), workflow.fixtureId)
    sequences.add(signature)
    workflow.steps.forEach((step, index) => {
      const task = tasks.get(step.taskId)
      assert.ok(task, workflow.fixtureId)
      assert.equal(task.collectionId, workflow.collectionId)
      assert.equal(step.sequenceOrder, index + 1)
      assert.ok(task.targetIds.includes(step.targetId))
      assert.equal(step.isManualStep, false)
      assert.ok(step.dataSource.length > 0 && step.dataOutput.length > 0)
    })
  }
})

test('coverage is professional and host/provider labels do not imply integrations', () => {
  const categories = new Map()
  const targets = new Set()
  const kinds = new Set()
  const contexts = new Set()
  const linkedMethods = new Set()
  for (const task of catalog.tasks) {
    categories.set(task.category, (categories.get(task.category) ?? 0) + 1)
    task.targetIds.forEach(id => targets.add(id))
    task.tags.forEach(tag => contexts.add(tag))
    kinds.add(task.taskType)
    linkedMethods.add(task.methodId)
  }
  assert.equal(categories.size, 11)
  for (const category of ['research', 'writing', 'meetings', 'data', 'presentations', 'coding', 'testing', 'knowledge', 'automation', 'desktop']) {
    assert.equal(categories.get(category), 20, category)
  }
  assert.equal(linkedMethods.size, counts.methods)
  assert.equal(targets.size, 11)
  assert.equal(kinds.size, 10)
  for (const host of ['microsoft.word', 'microsoft.excel', 'microsoft.powerpoint', 'microsoft.outlook',
    'microsoft.teams', 'microsoft.onenote', 'microsoft.pages', 'microsoft.notebooks', 'microsoft.onedrive',
    'microsoft.edge', 'microsoft.sharepoint', 'microsoft.loop']) {
    assert.ok(contexts.has(host), host)
    assert.ok(!targets.has(host), 'Context-only host must not be a runtime claim')
  }
  assert.ok(![...targets].some(id => /frontier/i.test(id)))
})

test('golden intents resolve IDs and hard target/format constraints without live model claims', () => {
  assert.equal(new Set(catalog.scenarios.map(item => item.query)).size, counts.scenarios)
  for (const scenario of catalog.scenarios) {
    assert.equal(scenario.collectionId, collectionId)
    assert.equal(scenario.liveModelExecution, 'not-run')
    assert.ok(scenario.query.length > 35)
    for (const id of scenario.expectedTaskIds) {
      const task = tasks.get(id)
      assert.equal(task.collectionId, scenario.collectionId)
      assert.equal(task.format, scenario.format)
      assert.equal(task.taskType, scenario.taskType)
      assert.ok(scenario.targetIds.every(target => task.targetIds.includes(target)))
    }
    for (const id of scenario.expectedMethodIds) assert.equal(methods.get(id).collectionId, scenario.collectionId)
    for (const id of scenario.expectedWorkflowIds) assert.equal(workflows.get(id).collectionId, scenario.collectionId)
    if (scenario.workflowTargetScope === 'all-steps-match-target') {
      for (const id of scenario.expectedWorkflowIds) {
        assert.ok(workflows.get(id).steps.every(step => scenario.targetIds.includes(step.targetId)), scenario.id)
      }
    }
    for (const id of scenario.forbiddenWorkflowIds) {
      assert.ok(workflows.has(id))
      assert.ok(!scenario.expectedWorkflowIds.includes(id))
    }
  }
})

test('inline native examples are syntactically bounded and not executable packages', () => {
  for (const task of catalog.tasks.filter(item => item.artifact.content)) {
    const content = task.artifact.content
    assert.ok(Buffer.byteLength(content) < 10_000)
    if (['mcp.tool-json', 'anthropic.plugin'].includes(task.format)) {
      assert.deepEqual(JSON.parse(task.inlineContent), JSON.parse(content))
    }
    if (task.format === 'agentskills.skill-md') {
      assert.match(content, /^---\nname: [a-z0-9]+(?:-[a-z0-9]+)*\ndescription: .+\n---\n/)
      const name = content.match(/\nname: (.+)\n/)[1]
      assert.ok(name.length <= 64)
      const description = content.match(/\ndescription: (.+)\n/)[1]
      assert.ok(description.length <= 1024)
    }
  }
  assert.ok(catalog.tasks.some(task => task.fixtureId === 'researcher-builtin-reference'
    && task.taskType === 'prompt' && task.format === 'text.plain'
    && !task.inlineContent.includes('declarativeAgent')))
})

test('public corpus has no credential-like tokens, private URLs or manufactured engagement', () => {
  const serialized = JSON.stringify(catalog)
  assert.doesNotMatch(serialized, /(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|eyJ[A-Za-z0-9_-]{20,}\.|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)/)
  assert.doesNotMatch(serialized, /https?:\/\/(?:localhost|127\.0\.0\.1|10\.\d|192\.168\.|[^/" ]+\.internal\b)/i)
  for (const item of [...catalog.tasks, ...catalog.methods, ...catalog.workflows]) {
    for (const field of ['createdBy', 'createdByUserId', 'ratings', 'comments', 'popularity', 'tenantId', 'email']) {
      assert.equal(item[field], undefined, `${item.fixtureId}.${field}`)
    }
  }
})
