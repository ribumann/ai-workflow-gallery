import {
  BrowserCacheLocation,
  InteractionRequiredAuthError,
  PublicClientApplication,
} from '@azure/msal-browser'

let client: Promise<PublicClientApplication> | undefined

function configuration() {
  const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID as string | undefined
  const apiClientId = import.meta.env.VITE_ENTRA_API_CLIENT_ID as string | undefined
  const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
  if (!clientId || !guid.test(clientId) || !apiClientId || !guid.test(apiClientId)) {
    throw new Error('Gallery sign-in is not configured. Contact the deployment administrator.')
  }
  return { clientId, scope: `api://${apiClientId}/Gallery.Access` }
}

async function getClient() {
  client ??= (async () => {
    const { clientId } = configuration()
    const application = new PublicClientApplication({
      auth: {
        clientId,
        authority: 'https://login.microsoftonline.com/organizations',
        redirectUri: window.location.origin,
        postLogoutRedirectUri: window.location.origin,
      },
      cache: { cacheLocation: BrowserCacheLocation.SessionStorage },
    })
    await application.initialize()
    const response = await application.handleRedirectPromise()
    const accounts = application.getAllAccounts()
    application.setActiveAccount(response?.account ?? (accounts.length === 1 ? accounts[0] : null))
    return application
  })()
  return client
}

export class SignInRequiredError extends Error {
  constructor() {
    super('Your sign-in has expired. Sign in again before retrying this operation.')
    this.name = 'SignInRequiredError'
  }
}

export async function acquireGalleryToken(): Promise<string | null> {
  const application = await getClient()
  const account = application.getActiveAccount()
  if (!account) return null
  try {
    const response = await application.acquireTokenSilent({
      account,
      scopes: [configuration().scope],
    })
    return response.accessToken
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) throw new SignInRequiredError()
    throw error
  }
}

export async function signIn() {
  const application = await getClient()
  await application.loginRedirect({ scopes: [configuration().scope] })
}

export async function reauthenticate() {
  const application = await getClient()
  const account = application.getActiveAccount()
  const response = await application.acquireTokenPopup({
    scopes: [configuration().scope],
    ...(account ? { account } : {}),
  })
  if (!response.account || account && response.account.homeAccountId !== account.homeAccountId) {
    throw new Error('Sign in with the same account to preserve the identity of this draft.')
  }
  application.setActiveAccount(response.account)
}

export async function signOut() {
  const application = await getClient()
  await application.logoutRedirect({ account: application.getActiveAccount() })
}
