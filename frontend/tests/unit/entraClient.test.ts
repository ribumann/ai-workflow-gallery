import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const msal = vi.hoisted(() => ({
  initialize: vi.fn(),
  handleRedirectPromise: vi.fn(),
  getAllAccounts: vi.fn(),
  setActiveAccount: vi.fn(),
  getActiveAccount: vi.fn(),
  acquireTokenSilent: vi.fn(),
  loginRedirect: vi.fn(),
  logoutRedirect: vi.fn(),
  acquireTokenPopup: vi.fn(),
  construct: vi.fn(),
}))

vi.unmock('../../src/auth/entraClient')
vi.mock('@azure/msal-browser', () => ({
  BrowserCacheLocation: { SessionStorage: 'sessionStorage' },
  InteractionRequiredAuthError: class extends Error {},
  PublicClientApplication: class {
    constructor(configuration: unknown) { msal.construct(configuration) }
    initialize = msal.initialize
    handleRedirectPromise = msal.handleRedirectPromise
    getAllAccounts = msal.getAllAccounts
    setActiveAccount = msal.setActiveAccount
    getActiveAccount = msal.getActiveAccount
    acquireTokenSilent = msal.acquireTokenSilent
    loginRedirect = msal.loginRedirect
    logoutRedirect = msal.logoutRedirect
    acquireTokenPopup = msal.acquireTokenPopup
  },
}))

const clientId = '11111111-1111-4111-8111-111111111111'
const apiClientId = '22222222-2222-4222-8222-222222222222'
const account = { homeAccountId: 'human.directory' }

beforeEach(() => {
  vi.resetModules()
  vi.resetAllMocks()
  vi.stubEnv('VITE_ENTRA_CLIENT_ID', clientId)
  vi.stubEnv('VITE_ENTRA_API_CLIENT_ID', apiClientId)
  msal.getAllAccounts.mockReturnValue([])
  msal.handleRedirectPromise.mockResolvedValue(null)
})
afterEach(() => vi.unstubAllEnvs())

describe('MSAL authorization-code PKCE boundary', () => {
  it('initializes organizations authority and exact API delegated scope without persisting client configuration tokens', async () => {
    const client = await import('../../src/auth/entraClient')
    await client.signIn()
    expect(msal.construct).toHaveBeenCalledWith({
      auth: {
        clientId, authority: 'https://login.microsoftonline.com/organizations',
        redirectUri: window.location.origin, postLogoutRedirectUri: window.location.origin,
      },
      cache: { cacheLocation: 'sessionStorage' },
    })
    expect(msal.initialize).toHaveBeenCalledOnce()
    expect(msal.loginRedirect).toHaveBeenCalledWith({ scopes: [`api://${apiClientId}/Gallery.Access`] })
  })

  it('uses the redirect account before silent token acquisition', async () => {
    msal.handleRedirectPromise.mockResolvedValue({ account })
    msal.getActiveAccount.mockReturnValue(account)
    msal.acquireTokenSilent.mockResolvedValue({ accessToken: 'ephemeral-test-token' })
    const client = await import('../../src/auth/entraClient')
    expect(await client.acquireGalleryToken()).toBe('ephemeral-test-token')
    expect(msal.setActiveAccount).toHaveBeenCalledWith(account)
    expect(msal.acquireTokenSilent).toHaveBeenCalledWith({
      account, scopes: [`api://${apiClientId}/Gallery.Access`],
    })
    expect(msal.loginRedirect).not.toHaveBeenCalled()
  })

  it('does not silently select among multiple cached accounts', async () => {
    msal.getAllAccounts.mockReturnValue([account, { homeAccountId: 'different.directory' }])
    const client = await import('../../src/auth/entraClient')
    expect(await client.acquireGalleryToken()).toBeNull()
    expect(msal.setActiveAccount).toHaveBeenCalledWith(null)
    expect(msal.acquireTokenSilent).not.toHaveBeenCalled()
  })

  it('reports expired interaction without redirecting or replaying operations', async () => {
    const { InteractionRequiredAuthError } = await import('@azure/msal-browser')
    msal.getActiveAccount.mockReturnValue(account)
    msal.acquireTokenSilent.mockRejectedValue(new InteractionRequiredAuthError('interaction_required'))
    const client = await import('../../src/auth/entraClient')
    await expect(client.acquireGalleryToken()).rejects.toBeInstanceOf(client.SignInRequiredError)
    expect(msal.loginRedirect).not.toHaveBeenCalled()
    expect(msal.acquireTokenSilent).toHaveBeenCalledOnce()
  })

  it('fails closed before invoking MSAL when application configuration is missing', async () => {
    vi.stubEnv('VITE_ENTRA_CLIENT_ID', '')
    const client = await import('../../src/auth/entraClient')
    await expect(client.acquireGalleryToken()).rejects.toThrow('not configured')
    expect(msal.construct).not.toHaveBeenCalled()
  })

  it('surfaces provider/network failure rather than returning an anonymous success', async () => {
    msal.getActiveAccount.mockReturnValue(account)
    msal.acquireTokenSilent.mockRejectedValue(new Error('Provider unreachable'))
    const client = await import('../../src/auth/entraClient')
    await expect(client.acquireGalleryToken()).rejects.toThrow('Provider unreachable')
  })

  it('renews in a popup without replacing the draft account or reloading the SPA', async () => {
    msal.getActiveAccount.mockReturnValue(account)
    msal.acquireTokenPopup.mockResolvedValue({ account })
    const client = await import('../../src/auth/entraClient')
    await client.reauthenticate()
    expect(msal.acquireTokenPopup).toHaveBeenCalledWith({
      account, scopes: [`api://${apiClientId}/Gallery.Access`],
    })
    expect(msal.loginRedirect).not.toHaveBeenCalled()
  })

  it('does not switch an open draft to a different account during recovery', async () => {
    msal.getActiveAccount.mockReturnValue(account)
    msal.acquireTokenPopup.mockResolvedValue({ account: { homeAccountId: 'different' } })
    const client = await import('../../src/auth/entraClient')
    await expect(client.reauthenticate()).rejects.toThrow('same account')
    expect(msal.setActiveAccount).not.toHaveBeenCalledWith({ homeAccountId: 'different' })
  })
})
