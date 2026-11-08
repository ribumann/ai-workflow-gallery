import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiClient, ApiError } from '../../src/services/apiClient'
import type { TaskFormInput, WorkflowSaveRequest } from '../../src/services/types'
import { method } from './methodFixtures'

afterEach(() => vi.unstubAllGlobals())

function mockFetch(body: unknown = method, status = 200) {
  const fetch = vi.fn().mockImplementation(
    async () =>
      new Response(status === 204 ? null : JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/json' },
      }),
  )
  vi.stubGlobal('fetch', fetch)
  return fetch
}

describe('Method API contracts', () => {
  it('uses bounded search parameters and encodes identifiers', async () => {
    const fetch = mockFetch()
    await apiClient.listMethods({ q: 'source & summary', page: 2, pageSize: 25 })
    expect(fetch.mock.calls[0][0]).toBe('/api/methods?q=source+%26+summary&page=2&pageSize=25&collectionId=public-techniques')
    await apiClient.getMethod('id/with slash')
    expect(fetch.mock.calls[1][0]).toBe('/api/methods/id%2Fwith%20slash?collectionId=public-techniques')
  })

  it('creates and updates only content plus the required concurrency version', async () => {
    const fetch = mockFetch()
    const content = { title: method.title, summary: method.summary, approach: method.approach }
    await apiClient.createMethod(content)
    expect(fetch.mock.calls[0][1].method).toBe('POST')
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual(content)
    await apiClient.updateMethod(method.id, { ...content, expectedVersion: method.version })
    expect(fetch.mock.calls[1][1].method).toBe('PUT')
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({
      ...content,
      expectedVersion: method.version,
    })
  })

  it('handles empty deletion responses and exposes reference conflict details', async () => {
    const fetch = mockFetch(undefined, 204)
    await expect(apiClient.deleteMethod(method.id)).resolves.toBeUndefined()
    expect(fetch.mock.calls[0][1].method).toBe('DELETE')
    const conflict = {
      message: 'Still referenced',
      referencedByTasks: [{ id: 'task', name: 'Task' }],
      referencedByWorkflows: [],
    }
    mockFetch(conflict, 409)
    await expect(apiClient.deleteMethod(method.id)).rejects.toMatchObject({
      status: 409,
      body: conflict,
    })
    mockFetch({ message: 'Missing' }, 404)
    await expect(apiClient.getMethod(method.id)).rejects.toBeInstanceOf(ApiError)
  })
})

describe('Presence-aware association serialization', () => {
  const taskInput: TaskFormInput = {
    name: 'Task',
    description: 'Summary',
    taskType: 'prompt',
    targetIds: ['microsoft.copilot.m365'], format: null, setup: null, prerequisites: null,
    tags: [],
    referenceLink: '',
    inlineContent: 'Prompt',
    dataSource: '',
    dataOutput: '',
    file: null,
  }
  const workflowInput: WorkflowSaveRequest = {
    name: 'Workflow',
    problemStatement: 'Problem',
    desiredOutcome: 'Outcome',
    steps: [],
  }

  it.each([undefined, null, method.id])(
    'preserves task methodId %s without changing multipart files',
    async (methodId) => {
      const fetch = mockFetch()
      const file = new File(['content'], 'prompt.md', { type: 'text/markdown' })
      await apiClient.updateTask('task', { ...taskInput, methodId, file })
      const body = fetch.mock.calls[0][1].body as FormData
      const metadata = JSON.parse(body.get('metadata') as string)
      if (methodId === undefined) expect(metadata).not.toHaveProperty('methodId')
      else expect(metadata.methodId).toBe(methodId)
      expect(body.get('file')).toBe(file)
    },
  )

  it.each([undefined, null, method.id])(
    'preserves workflow methodId %s without changing steps',
    async (methodId) => {
      const fetch = mockFetch()
      await apiClient.updateWorkflow('workflow', { ...workflowInput, methodId })
      const body = JSON.parse(fetch.mock.calls[0][1].body)
      if (methodId === undefined) expect(body).not.toHaveProperty('methodId')
      else expect(body.methodId).toBe(methodId)
      expect(body.steps).toEqual([])
    },
  )

  it('leaves old-client and composer create requests unlinked by omission', async () => {
    const fetch = mockFetch()
    await apiClient.createTask(taskInput)
    await apiClient.createWorkflow(workflowInput)
    expect(JSON.parse(fetch.mock.calls[0][1].body.get('metadata'))).not.toHaveProperty('methodId')
    expect(JSON.parse(fetch.mock.calls[1][1].body)).not.toHaveProperty('methodId')
  })
})
