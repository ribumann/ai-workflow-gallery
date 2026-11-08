import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Button, MessageBar, MessageBarBody, MessageBarActions } from '@fluentui/react-components'
import { AuthContext, type AuthState, type SessionResponse } from './authContext'
import { ApiError, apiClient } from '../services/apiClient'
import { signIn, signOut, reauthenticate, SignInRequiredError } from './entraClient'
import { onReauthenticationRequired } from './authenticationEvents'

const NOT_SIGNED_IN: SessionResponse = { isAuthenticated: false, isAuthorized: false, displayName: null }

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<SessionResponse>(NOT_SIGNED_IN)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [needsReauthentication, setNeedsReauthentication] = useState(false)
  const [recovering, setRecovering] = useState(false)
  const [recoveryError, setRecoveryError] = useState<string | null>(null)

  useEffect(() => onReauthenticationRequired(() => setNeedsReauthentication(true)), [])

  async function recover() {
    setRecovering(true)
    setRecoveryError(null)
    try {
      // Popup recovery keeps editors mounted and never replays the failed request.
      await reauthenticate()
      const checked = await apiClient.getSession()
      if (!checked.isAuthenticated || !checked.isAuthorized) throw new Error('Access was not renewed.')
      setSession(checked)
      setNeedsReauthentication(false)
    } catch {
      setRecoveryError('Sign-in could not be renewed. Your draft is still open. Try again with the same account.')
    } finally {
      setRecovering(false)
    }
  }

  useEffect(() => {
    let cancelled = false
    apiClient.getSession()
      .then((data) => {
        if (!cancelled) setSession(data)
      })
      .catch((failure: unknown) => {
        if (cancelled) return
        if (failure instanceof ApiError && failure.status === 403) {
          setSession({ isAuthenticated: true, isAuthorized: false, displayName: null })
        } else if (failure instanceof SignInRequiredError ||
          failure instanceof ApiError && failure.status === 401) {
          setSession(NOT_SIGNED_IN)
        } else {
          setError('Sign-in services are unavailable or not configured. Your access has not been checked. Try again later or contact the administrator.')
        }
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false)
      })
    return () => { cancelled = true }
  }, [])

  const value = useMemo<AuthState>(() => {
    const run = async (operation: () => Promise<void>) => {
      try {
        await operation()
      } catch {
        setError('The sign-in operation could not be completed. Try again later or contact the administrator.')
      }
    }
    return {
      isLoading,
      isAuthenticated: session.isAuthenticated,
      isAuthorized: session.isAuthorized,
      displayName: session.displayName,
      publicDemo: session.publicDemo === true,
      composerEnabled: session.composerEnabled === true,
      composerState: session.composerState,
      composerMaxGoalCharacters: session.composerMaxGoalCharacters,
      collectionCapabilities: session.collectionCapabilities,
      error,
      signIn: () => run(signIn),
      signOut: () => run(signOut),
      refreshPublicSession: async () => {
        if (session.publicDemo !== true) throw new Error('Public readiness is not available in private mode.')
        const checked = await apiClient.getSession()
        if (checked.publicDemo !== true) throw new Error('The server did not confirm public readiness.')
        setSession(checked)
      },
    }
  }, [isLoading, session, error])

  return <AuthContext.Provider value={value}>
    {needsReauthentication && session.isAuthenticated && session.isAuthorized && (
      <MessageBar intent="warning" role="alert">
        <MessageBarBody>
          {recoveryError ?? 'Your sign-in has expired. Your draft is still open. Sign in again, then retry the operation yourself.'}
        </MessageBarBody>
        <MessageBarActions>
          <Button disabled={recovering} onClick={() => void recover()}>
            {recovering ? 'Renewing sign-in...' : 'Sign in again'}
          </Button>
        </MessageBarActions>
      </MessageBar>
    )}
    {children}
  </AuthContext.Provider>
}
