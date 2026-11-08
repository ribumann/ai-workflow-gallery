import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const reset = await readFile(new URL('../deployment/playground/reset-playground.sql', import.meta.url), 'utf8')
const normalized = reset.replace(/\s+/g, ' ')
const firstDelete = normalized.indexOf('DELETE FROM dbo.Comments')

test('policy dynamic SQL escapes every nested THROW message', async () => {
  const policy = (await readFile(new URL('../deployment/playground/apply-playground-policy.sql', import.meta.url), 'utf8'))
    .replace(/\r/g, '')
  const blocks = [...policy.matchAll(/EXEC\(N'([\s\S]*?)\nEND'\);/g)]
  assert.equal(blocks.length, 6)
  let messages = 0
  for (const [, block] of blocks) {
    for (const line of block.split(/\r?\n/).filter((line) => /\bTHROW\b/.test(line))) {
      assert.match(line, /^\s*THROW \d+, N''[^']*'', 1;$/)
      messages++
    }
  }
  assert.equal(messages, 8)
})

test('Playground reset checks cross-collection references before any deletion', () => {
  const transaction = normalized.indexOf('BEGIN TRANSACTION;')
  const serializable = normalized.indexOf('SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;')
  assert.ok(serializable >= 0 && serializable < transaction)
  assert.ok(transaction >= 0)
  assert.ok(firstDelete > transaction)

  const preflight = normalized.slice(transaction, firstDelete)
  for (const [table, code] of [
    ['WorkflowStep-to-Workflow', '51006'],
    ['WorkflowStep-to-Task', '51007'],
    ['Task/Workflow-to-Method', '51008'],
    ['Comment', '51009'],
    ['Rating', '51010'],
  ]) {
    const throwAt = preflight.indexOf(`THROW ${code},`)
    assert.ok(throwAt >= 0, `Missing fail-closed preflight for ${table}.`)
    assert.ok(throwAt < preflight.length, `${table} preflight must precede the first delete.`)
  }

  for (const [left, right] of [
    ['step.CollectionId', 'workflow.CollectionId'],
    ['step.CollectionId', 'task.CollectionId'],
    ['task.CollectionId', 'method.CollectionId'],
    ['workflow.CollectionId', 'method.CollectionId'],
  ]) {
    assert.ok(preflight.includes(`${left} = N'playground' AND ${right} <> N'playground'`))
    assert.ok(preflight.includes(`${left} <> N'playground' AND ${right} = N'playground'`))
  }
})

test('Playground reset rejects engagement rows that point across collection boundaries', () => {
  const commentsStart = normalized.indexOf('FROM dbo.Comments AS engagement')
  const ratingsStart = normalized.indexOf('FROM dbo.Ratings AS engagement')
  const firstDelete = normalized.indexOf('DELETE FROM dbo.Comments')
  assert.ok(commentsStart >= 0 && ratingsStart > commentsStart && firstDelete > ratingsStart)

  for (const block of [
    normalized.slice(commentsStart, ratingsStart),
    normalized.slice(ratingsStart, firstDelete),
  ]) {
    assert.ok(block.includes("engagement.CollectionId = N'playground'"))
    assert.ok(block.includes("engagement.CollectionId <> N'playground'"))
    for (const [subjectType, table] of [
      [0, 'Tasks'],
      [1, 'Workflows'],
      [2, 'AiMethods'],
    ]) {
      assert.ok(block.includes(`engagement.SubjectType = ${subjectType}`))
      assert.ok(block.includes(`FROM dbo.${table} AS target`))
    }
    assert.ok(block.includes("target.CollectionId <> N'playground'"))
    assert.ok(block.includes("target.CollectionId = N'playground'"))
  }
})

test('every reset delete is explicitly scoped to Playground', () => {
  const deletes = [...normalized.matchAll(/DELETE FROM dbo\.(\w+)[^;]*;/g)]
  assert.deepEqual(deletes.map((match) => match[1]), [
    'Comments', 'Ratings', 'Workflows', 'Tasks', 'AiMethods', 'ReportLinks',
  ])
  for (const statement of deletes)
    assert.ok(statement[0].includes("WHERE CollectionId = N'playground'"))
})
