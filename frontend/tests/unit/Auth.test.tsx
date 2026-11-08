import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../../src/auth/AuthProvider'
import { RequireAuth } from '../../src/auth/RequireAuth'
import { apiClient } from '../../src/services/apiClient'
import { acquireGalleryToken, reauthenticate, SignInRequiredError } from '../../src/auth/entraClient'

function renderGuard() {
  return render(<FluentProvider theme={webLightTheme}><AuthProvider>
    <RequireAuth><div>Protected catalog</div></RequireAuth>
  </AuthProvider></FluentProvider>)
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.clearAllMocks()
  window.sessionStorage.clear()
  window.history.replaceState({}, '', '/')
})

describe('shared bearer HTTP client', () => {
  it('acquires a token and sends it only as a bearer header', async () => {
    vi.mocked(acquireGalleryToken).mockResolvedValueOnce('in-memory-test-token')
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [] })))
    vi.stubGlobal('fetch', fetch)
    await apiClient.listTasks()
    const [url, init] = fetch.mock.calls[0]
    expect(url).not.toContain('in-memory-test-token')
    expect(init.headers.Authorization).toBe('Bearer in-memory-test-token')
  })

  it('does not automatically replay a write after uncertain delivery', async () => {
    const fetch = vi.fn().mockRejectedValue(new TypeError('Network failure'))
    vi.stubGlobal('fetch', fetch)
    await expect(apiClient.createMethod({ title: 'Draft', summary: 'Summary', approach: 'Approach' })).rejects.toThrow('Network failure')
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})

describe('authentication states', () => {
  it('bootstraps private collection authorization with the actual scoped session request', async () => {
    window.history.replaceState({}, '', '/?collectionId=curated-tools')
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      isAuthenticated: true, isAuthorized: true, displayName: 'Collection reader', publicDemo: false,
      collectionCapabilities: {
        id: 'curated-tools', label: 'Curated tools', description: 'Reviewed originals',
        canWrite: false, isDisposable: false,
      },
    })))
    vi.stubGlobal('fetch', fetch)
    renderGuard()
    expect(await screen.findByText('Protected catalog')).toBeVisible()
    expect(fetch).toHaveBeenCalledOnce()
    expect(fetch.mock.calls[0][0]).toBe('/api/session?collectionId=curated-tools')
  })

  it('does not render protected data for a 401 and offers explicit sign-in', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 401 })))
    renderGuard()
    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeVisible()
    expect(screen.queryByText('Protected catalog')).not.toBeInTheDocument()
  })

  it('distinguishes a forbidden directory from signed-out state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 403 })))
    renderGuard()
    expect(await screen.findByText('This account is not authorized')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
  })

  it.each([503, 500])('surfaces HTTP %s as unavailable, never as signed out', async (status) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status })))
    renderGuard()
    expect(await screen.findByRole('alert')).toHaveTextContent('Sign-in unavailable')
    expect(screen.queryByText('Protected catalog')).not.toBeInTheDocument()
  })

  it('surfaces network errors explicitly', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Network error')))
    renderGuard()
    expect(await screen.findByRole('alert')).toHaveTextContent('Your access has not been checked')
  })

  it('treats a malformed session response as unavailable, not authorized or signed out', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      isAuthenticated: 'true', isAuthorized: true,
    }))))
    renderGuard()
    expect(await screen.findByRole('alert')).toHaveTextContent('Sign-in unavailable')
    expect(screen.queryByText('Protected catalog')).not.toBeInTheDocument()
  })

  it('renders the catalog only after server authorization succeeds', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      isAuthenticated: true, isAuthorized: true, displayName: 'Test person',
    }))))
    renderGuard()
    await waitFor(() => expect(screen.getByText('Protected catalog')).toBeVisible())
  })

  it('preserves a mounted draft on later expiry and offers popup recovery without replay', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      isAuthenticated: true, isAuthorized: true, displayName: 'Test person',
    }))))
    vi.stubGlobal('fetch', fetch)
    render(<FluentProvider theme={webLightTheme}><AuthProvider>
      <RequireAuth><input aria-label="Unsaved draft" defaultValue="Keep my draft" /></RequireAuth>
    </AuthProvider></FluentProvider>)
    const draft = await screen.findByRole('textbox', { name: 'Unsaved draft' })
    vi.mocked(acquireGalleryToken).mockRejectedValueOnce(new SignInRequiredError())
    await act(async () => {
      await expect(apiClient.createMethod({ title: 'Draft', summary: 'Summary', approach: 'Approach' })).rejects.toBeInstanceOf(SignInRequiredError)
    })
    expect(screen.getByRole('alert')).toHaveTextContent('Your draft is still open')
    expect(screen.getByRole('textbox', { name: 'Unsaved draft' })).toBe(draft)
    expect(draft).toHaveValue('Keep my draft')
    vi.mocked(reauthenticate).mockResolvedValueOnce()
    fireEvent.click(screen.getByRole('button', { name: 'Sign in again' }))
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(reauthenticate).toHaveBeenCalledOnce()
    expect(fetch).toHaveBeenCalledTimes(2) // Session checks only; the write was never sent/replayed.
    expect(screen.getByRole('textbox', { name: 'Unsaved draft' })).toBe(draft)
  })

  it('keeps the draft mounted when popup recovery fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      isAuthenticated: true, isAuthorized: true, displayName: null,
    }))))
    render(<FluentProvider theme={webLightTheme}><AuthProvider>
      <RequireAuth><input aria-label="Unsaved draft" defaultValue="Keep my draft" /></RequireAuth>
    </AuthProvider></FluentProvider>)
    await screen.findByRole('textbox', { name: 'Unsaved draft' })
    vi.mocked(acquireGalleryToken).mockRejectedValueOnce(new SignInRequiredError())
    await act(async () => {
      await expect(apiClient.listTasks()).rejects.toBeInstanceOf(SignInRequiredError)
    })
    vi.mocked(reauthenticate).mockRejectedValueOnce(new Error('Popup cancelled'))
    fireEvent.click(screen.getByRole('button', { name: 'Sign in again' }))
    expect(await screen.findByText(/Sign-in could not be renewed/)).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'Unsaved draft' })).toHaveValue('Keep my draft')
  })
})
