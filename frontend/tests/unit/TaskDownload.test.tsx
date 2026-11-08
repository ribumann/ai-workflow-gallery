import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiClient, ApiError } from '../../src/services/apiClient'
import { acquireGalleryToken, SignInRequiredError } from '../../src/auth/entraClient'
import { onReauthenticationRequired } from '../../src/auth/authenticationEvents'
import { AuthProvider } from '../../src/auth/AuthProvider'
import { RequireAuth } from '../../src/auth/RequireAuth'
import { TaskDetailPage } from '../../src/pages/TaskDetail/TaskDetailPage'
import { task } from './methodFixtures'

vi.mock('../../src/components/RatingWidget', () => ({ RatingWidget: () => null }))
vi.mock('../../src/components/CommentsSection', () => ({ CommentsSection: () => null }))

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})

describe('authenticated binary client', () => {
  it('uses the encoded trusted endpoint and preserves non-JSON bytes and type', async () => {
    vi.mocked(acquireGalleryToken).mockResolvedValueOnce('download-test-token')
    const bytes = new Uint8Array([0, 255, 128, 13, 10, 123])
    const fetch = vi.fn().mockResolvedValue(new Response(bytes, {
      headers: { 'Content-Type': 'application/zip' },
    }))
    vi.stubGlobal('fetch', fetch)

    const blob = await apiClient.getTaskFile('task/with?reserved#characters')
    expect(fetch).toHaveBeenCalledWith(
      '/api/tasks/task%2Fwith%3Freserved%23characters/file?collectionId=public-techniques',
      expect.objectContaining({
        cache: 'no-store',
        headers: { Accept: 'application/octet-stream', Authorization: 'Bearer download-test-token' },
      }),
    )
    expect(blob.type).toBe('application/zip')
    expect(new Uint8Array(await blob.arrayBuffer())).toEqual(bytes)
  })

  it.each([401, 403, 404, 500])('propagates HTTP %s and only requests reauthentication for 401', async (status) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"error":"unavailable"}', { status })))
    const listener = vi.fn()
    const unsubscribe = onReauthenticationRequired(listener)
    try {
      await expect(apiClient.getTaskFile(task.id)).rejects.toMatchObject({
        name: 'ApiError', status, body: { error: 'unavailable' },
      })
      expect(listener).toHaveBeenCalledTimes(status === 401 ? 1 : 0)
    } finally {
      unsubscribe()
    }
  })

  it('preserves non-JSON HTTP errors and network failures without retrying', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response('Service unavailable', { status: 503 }))
      .mockRejectedValueOnce(new TypeError('Network failure'))
    vi.stubGlobal('fetch', fetch)
    await expect(apiClient.getTaskFile(task.id)).rejects.toBeInstanceOf(ApiError)
    await expect(apiClient.getTaskFile(task.id)).rejects.toThrow('Network failure')
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('signals interaction-required token acquisition without sending a request', async () => {
    const fetch = vi.fn()
    vi.stubGlobal('fetch', fetch)
    vi.mocked(acquireGalleryToken).mockRejectedValueOnce(new SignInRequiredError())
    const listener = vi.fn()
    const unsubscribe = onReauthenticationRequired(listener)
    try {
      await expect(apiClient.getTaskFile(task.id)).rejects.toBeInstanceOf(SignInRequiredError)
      expect(listener).toHaveBeenCalledOnce()
      expect(fetch).not.toHaveBeenCalled()
    } finally {
      unsubscribe()
    }
  })
})

function renderDetail() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={[`/tasks/${task.id}`]}>
        <AuthProvider>
          <RequireAuth>
            <Routes><Route path="/tasks/:id" element={<TaskDetailPage />} /></Routes>
          </RequireAuth>
        </AuthProvider>
      </MemoryRouter>
    </FluentProvider>,
  )
}

function mockDetail(fileResponse: () => Promise<Response>) {
  const fetch = vi.fn().mockImplementation((path: string) => {
    if (path === '/api/session?collectionId=public-techniques') {
      return Promise.resolve(new Response(JSON.stringify({
        isAuthenticated: true, isAuthorized: true, displayName: 'Test user',
      })))
    }
    if (path === `/api/tasks/${task.id}?collectionId=public-techniques`) {
      return Promise.resolve(new Response(JSON.stringify({
        ...task,
        attachedFile: { ...task.attachedFile, downloadUrl: 'https://untrusted.example/file' },
      })))
    }
    if (path === `/api/tasks/${task.id}/file?collectionId=public-techniques`) return fileResponse()
    throw new Error(`Unexpected request: ${path}`)
  })
  vi.stubGlobal('fetch', fetch)
  return fetch
}

describe('task attachment action', () => {
  it('does not present a server failure as a deleted task', async () => {
    const fetch = vi.fn().mockImplementation((path: string) => {
      if (path === '/api/session?collectionId=public-techniques') {
        return Promise.resolve(new Response(JSON.stringify({
          isAuthenticated: true, isAuthorized: true, displayName: 'Test user',
        })))
      }
      if (path === `/api/tasks/${task.id}?collectionId=public-techniques`) {
        return Promise.resolve(new Response('{"error":"unavailable"}', { status: 500 }))
      }
      throw new Error(`Unexpected request: ${path}`)
    })
    vi.stubGlobal('fetch', fetch)

    renderDetail()

    expect(await screen.findByText('The task could not be loaded. Please try again.')).toBeInTheDocument()
    expect(screen.queryByText('This task could not be found. It may have been deleted.')).not.toBeInTheDocument()
  })

  it('shows pending state, downloads with the displayed name, and releases the object URL', async () => {
    let finish!: (response: Response) => void
    const fetch = mockDetail(() => new Promise((resolve) => { finish = resolve }))
    const createUrl = vi.fn().mockReturnValue('blob:download-test')
    const revokeUrl = vi.fn()
    vi.stubGlobal('URL', class extends URL {
      static createObjectURL = createUrl
      static revokeObjectURL = revokeUrl
    })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      expect(this.download).toBe('prompt.md')
      expect(this.href).toBe('blob:download-test')
      expect(this.isConnected).toBe(true)
    })
    renderDetail()
    const button = await screen.findByRole('button', { name: 'prompt.md (128 B)' })
    fireEvent.click(button)
    expect(button).toBeDisabled()
    expect(screen.getByText('Downloading file...')).toBeInTheDocument()
    fireEvent.click(button)
    await waitFor(() => expect(fetch.mock.calls.filter(([path]) => path.endsWith('/file?collectionId=public-techniques'))).toHaveLength(1))
    finish(new Response(new Uint8Array([0, 255, 128]), { headers: { 'Content-Type': 'application/octet-stream' } }))
    await waitFor(() => expect(button).toBeEnabled())
    expect(click).toHaveBeenCalledOnce()
    expect(createUrl).toHaveBeenCalledOnce()
    const blob = createUrl.mock.calls[0][0] as Blob
    expect(new Uint8Array(await blob.arrayBuffer())).toEqual(new Uint8Array([0, 255, 128]))
    await waitFor(() => expect(revokeUrl).toHaveBeenCalledWith('blob:download-test'))
    expect(document.querySelector('a[download]')).toBeNull()
    expect(fetch.mock.calls.every(([path]) => path.startsWith('/api/'))).toBe(true)
    expect(screen.getByRole('heading', { name: task.name })).toBeVisible()
  })

  it.each([401, 500])('keeps detail mounted on HTTP %s and allows only an explicit retry', async (status) => {
    const response = vi.fn().mockResolvedValue(new Response('{}', { status }))
    mockDetail(response)
    renderDetail()
    const heading = await screen.findByRole('heading', { name: task.name })
    const button = screen.getByRole('button', { name: 'prompt.md (128 B)' })
    fireEvent.click(button)
    expect(await screen.findByText(status === 401
      ? 'Sign in again, then retry downloading this file.'
      : 'This file could not be downloaded. Try again.')).toBeVisible()
    expect(screen.getByRole('heading', { name: task.name })).toBe(heading)
    expect(button).toBeEnabled()
    expect(response).toHaveBeenCalledOnce()
    if (status === 401) expect(screen.getByRole('button', { name: 'Sign in again' })).toBeVisible()
    response.mockImplementation(() => Promise.reject(new TypeError('Offline')))
    fireEvent.click(button)
    expect(await screen.findByText('This file could not be downloaded. Try again.')).toBeVisible()
    expect(response).toHaveBeenCalledTimes(2)
  })

  it('aborts an in-flight download when the detail unmounts', async () => {
    const fetch = mockDetail(() => new Promise(() => {}))
    const view = renderDetail()
    fireEvent.click(await screen.findByRole('button', { name: 'prompt.md (128 B)' }))
    await waitFor(() => expect(fetch.mock.calls.some(([path]) => path.endsWith('/file?collectionId=public-techniques'))).toBe(true))
    const [, init] = fetch.mock.calls.find(([path]) => path.endsWith('/file?collectionId=public-techniques'))!
    expect(init.signal.aborted).toBe(false)
    view.unmount()
    expect(init.signal.aborted).toBe(true)
  })
})
