import { afterEach, expect, it, vi } from 'vitest'
import { apiClient } from '../../src/services/apiClient'
import { acquireGalleryToken } from '../../src/auth/entraClient'

const target = vi.hoisted(() => ({ collectionId: 'public-techniques' }))
vi.mock('../../src/services/deployment', () => ({
  publicDemo: true,
  selectedCollection: () => target.collectionId,
  scopedPath: (path: string) =>
    `${path}${path.includes('?') ? '&' : '?'}collectionId=${target.collectionId}`,
}))

afterEach(() => {
  vi.unstubAllGlobals()
  vi.clearAllMocks()
  target.collectionId = 'public-techniques'
})

it('permits only the anonymous proposal POST and never acquires or sends a bearer token', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response('{}'))
  vi.stubGlobal('fetch', fetch)
  await apiClient.composeWorkflow({ goal: 'Review evidence' })
  expect(fetch).toHaveBeenCalledOnce()
  expect(fetch.mock.calls[0][0]).toBe('/api/ai/compose?collectionId=public-techniques')
  expect(fetch.mock.calls[0][1].headers.Authorization).toBeUndefined()
  expect(acquireGalleryToken).not.toHaveBeenCalled()
})

it('sends anonymous catalog writes to the selected collection without a bearer token', async () => {
  target.collectionId = 'playground'
  const fetch = vi.fn().mockResolvedValue(new Response('{}', { status: 201 }))
  vi.stubGlobal('fetch', fetch)
  await apiClient.createMethod({ title: 'Playground method', summary: 'Summary', approach: 'Approach' })
  expect(fetch).toHaveBeenCalledOnce()
  expect(fetch.mock.calls[0][0]).toBe('/api/methods?collectionId=playground')
  expect(fetch.mock.calls[0][1].headers.Authorization).toBeUndefined()
  expect(acquireGalleryToken).not.toHaveBeenCalled()
})

it('preserves server authorization for anonymous writes in published collections', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response('{"error":"forbidden"}', { status: 403 }))
  vi.stubGlobal('fetch', fetch)
  await expect(apiClient.createMethod({ title: 'Forbidden', summary: 'Summary', approach: 'Approach' }))
    .rejects.toMatchObject({ status: 403 })
  expect(fetch).toHaveBeenCalledOnce()
  expect(fetch.mock.calls[0][0]).toBe('/api/methods?collectionId=public-techniques')
  expect(fetch.mock.calls[0][1].headers.Authorization).toBeUndefined()
  expect(acquireGalleryToken).not.toHaveBeenCalled()
})
