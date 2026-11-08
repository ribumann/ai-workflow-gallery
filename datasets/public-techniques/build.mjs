import { createHash } from 'node:crypto'
import { readFileSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { checkedAt, sources } from './sources.mjs'
import { methods as authoredMethods } from './methods.mjs'
import { recipes } from './recipes.mjs'
import { extensions } from './extensions.mjs'
import { journeys, goldenIntents } from './journeys.mjs'

export const datasetId = 'public-techniques-v1'
export const collectionId = 'public-techniques'
export const corpusPath = fileURLToPath(new URL('./catalog.json', import.meta.url))
export const counts = { tasks: 225, workflows: 37, methods: 15, scenarios: 44, sources: 29 }

export function stableId(type, slug) {
  const hex = createHash('sha256').update(`${datasetId}/${type}/${slug}`).digest('hex')
  // RFC 9562 custom UUID version 8; the namespaced hash is not an identity claim.
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-8${hex.slice(13, 16)}-a${hex.slice(17, 20)}-${hex.slice(20, 32)}`
}

function sourceNotes(sourceIds) {
  return sourceIds.map(id => {
    const source = sources.find(item => item.id === id)
    if (!source) throw new Error(`Unknown source: ${id}`)
    return `${source.title} (${source.url}; checked ${source.checkedAt})`
  }).join('\n')
}

export function buildCatalog() {
  const methods = authoredMethods.map(method => ({
    ...method, id: stableId('method', method.fixtureId), collectionId,
    approach: `${method.approach}\n\nPublic technique references:\n${sourceNotes(method.sourceIds)}\n\nOriginal method summary. Sources support the technique, not a live gallery connection.`,
  }))
  const methodBySlug = new Map(methods.map(item => [item.fixtureId, item]))
  const tasks = [...recipes, ...extensions].map(recipe => {
    const method = methodBySlug.get(recipe.method)
    if (!method) throw new Error(`Unknown method: ${recipe.method}`)
    const sourceIds = [...new Set([
      ...recipe.sourceIds,
      ...(recipe.fixtureId === 'ci-failure-triage' || recipe.fixtureId === 'test-isolation' ? ['github-ci'] : []),
      ...(recipe.category === 'testing' ? ['github-unit-tests'] : []),
      ...(recipe.fixtureId === 'claim-ledger' ? ['github-research'] : []),
    ])]
    const applicability = `Use when you need to ${recipe.name.charAt(0).toLowerCase()}${recipe.name.slice(1)}.`
    const steps = [
      `Select only authorized or synthetic inputs: ${recipe.inputs}.`,
      recipe.technique,
      `Review before reuse: ${recipe.review}.`,
    ]
    const provenance = sourceNotes(sourceIds)
    const inlineContent = recipe.content && ['anthropic.plugin', 'mcp.tool-json'].includes(recipe.format)
      ? recipe.content
      : recipe.content
      ? `${recipe.content}\n\n---\n\nExample applicability: ${applicability}\nInputs: ${recipe.inputs}.\nSetup: ${recipe.technique}.\nExpected output: ${recipe.output}.\nReview: ${recipe.review}.\n\nPublic references:\n${provenance}`
      : `# ${recipe.name}\n\n${applicability}\n\nInputs: ${recipe.inputs}.\n\nSteps:\n${steps.map((step, index) => `${index + 1}. ${step}`).join('\n')}\n\nExpected output: ${recipe.output}.\n\nReview criteria: ${recipe.review}.\n\nPublic references:\n${provenance}`
    const preview = recipe.targetIds.some(target => target === 'microsoft.scout.desktop' || target === 'microsoft.copilot.code')
    return {
      id: stableId('task', recipe.fixtureId), fixtureId: recipe.fixtureId, collectionId,
      name: recipe.name, description: `${recipe.technique}. Output: ${recipe.output}. Review: ${recipe.review}.`,
      taskType: recipe.taskType, targetIds: recipe.targetIds, format: recipe.format,
      setup: recipe.content ? recipe.technique : 'Copy these original instructions into the selected documented experience after reviewing account access and permitted input scope.',
      prerequisites: `${preview ? 'Preview/Frontier access and supported platform required. ' : ''}Account, license, host version and administrator policy must permit this technique. No live gallery connection, installation or execution has been verified. External actions require separate explicit approval.`,
      tags: [datasetId, recipe.category, ...recipe.context],
      referenceLink: sources.find(source => source.id === sourceIds[0]).url,
      inlineContent, dataSource: recipe.inputs, dataOutput: recipe.output,
      methodId: method.id, sourceIds, category: recipe.category,
      technique: recipe.technique, applicability, steps, reviewCriteria: [recipe.review],
      artifact: {
        kind: recipe.taskType, format: recipe.format,
        form: recipe.content ? 'inline-example' : recipe.taskType === 'prompt' ? 'copyable-instructions' : 'reference-only',
        content: recipe.content ?? null,
        output: recipe.output,
      },
      compatibility: {
        evidence: recipe.support ?? 'documented-technique',
        installation: 'not-installed',
        galleryConnection: 'not-verified',
        note: recipe.format === null
          ? 'Reference-only design procedure; no downloadable native definition or runtime implementation is supplied.'
          : 'Format is provider/host-specific where documented. A shared name does not imply package portability or an available live integration.',
      },
    }
  })
  const taskBySlug = new Map(tasks.map(item => [item.fixtureId, item]))
  const workflows = journeys.map(journey => ({
    id: stableId('workflow', journey.fixtureId), fixtureId: journey.fixtureId, collectionId,
    name: journey.name, problemStatement: journey.problemStatement,
    desiredOutcome: `${journey.desiredOutcome}. Human review is required; this ordered workflow does not execute tools.`,
    methodId: methodBySlug.get(journey.method).id,
    sourceIds: [...new Set(journey.taskRefs.flatMap(slug => {
      const task = taskBySlug.get(slug)
      if (!task) throw new Error(`Unknown workflow task: ${slug}`)
      return task.sourceIds
    }))],
    steps: journey.taskRefs.map((slug, index) => {
      const task = taskBySlug.get(slug)
      return {
        sequenceOrder: index + 1, taskId: task.id, targetId: task.targetIds[0],
        notes: `Review this step's output before proceeding. ${index ? 'Carry forward only approved relevant output from earlier steps; also supply the task-specific inputs.' : 'Confirm the source scope before starting.'}`,
        isManualStep: false, manualStepTitle: null, manualStepInstructions: null,
        dataSource: task.dataSource, dataOutput: task.dataOutput,
      }
    }),
  }))
  const workflowBySlug = new Map(workflows.map(item => [item.fixtureId, item]))
  const scenarios = goldenIntents.map(intent => {
    const task = taskBySlug.get(intent.taskRef)
    const workflow = workflowBySlug.get(intent.workflowRef)
    if (!task || !workflow) throw new Error(`Unresolved golden: ${intent.fixtureId}`)
    return {
      id: intent.fixtureId, collectionId, query: intent.query,
      expectedTaskIds: [task.id], expectedMethodIds: [task.methodId], expectedWorkflowIds: [workflow.id],
      targetIds: [task.targetIds[0]], format: task.format, taskType: task.taskType,
      workflowTargetScope: Number(intent.fixtureId.slice(-2)) >= 34 && !['intent-37', 'intent-38'].includes(intent.fixtureId)
        ? 'all-steps-match-target' : 'related-cross-target-workflow',
      forbiddenWorkflowIds: intent.fixtureId === 'intent-42'
        ? [workflowBySlug.get('desktop-task-boundary').id] : [],
      assertions: ['expected-task-in-candidate-set', 'task-matches-target-and-format', 'method-resolves-in-collection', 'workflow-resolves-in-collection'],
      liveModelExecution: 'not-run',
    }
  })
  return {
    schemaVersion: 1, datasetId, collectionId, checkedAt,
    contentPolicy: {
      authorship: 'Original concise recipes and examples inspired by reviewed public primary documentation; no prompt library, source code or long published example copied.',
      sourceScope: 'Public documentation only. Several techniques share a source; source count is not technique count.',
      safety: 'Synthetic or explicitly authorized inputs only. No identities, engagement, roles, secrets, installations, external calls or live service results are seeded.',
      import: 'Maintainer-only explicit fresh collection import. No automatic import, anonymous publication or live UAT reset.',
    },
    sources, methods, tasks, workflows, scenarios,
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2)
  if (args.some(arg => arg !== '--check') || args.length > 1) {
    throw new Error('Usage: node datasets/public-techniques/build.mjs [--check]')
  }
  const rendered = `${JSON.stringify(buildCatalog(), null, 2)}\n`
  if (args.includes('--check')) {
    if (readFileSync(corpusPath, 'utf8') !== rendered) throw new Error('Catalog is stale. Rebuild from authored recipes.')
    console.log('Checked-in catalog matches authored recipes.')
  } else {
    writeFileSync(corpusPath, rendered)
    console.log(`Generated ${datasetId}: ${JSON.stringify(counts)}.`)
  }
}
