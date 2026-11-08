import { createContext } from 'react'

/** The API reports a session only after cryptographic bearer validation and admission. */
export interface SessionResponse {
  isAuthenticated: boolean
  isAuthorized: boolean
  displayName: string | null
  publicDemo?: boolean
  composerEnabled?: boolean
  composerState?: 'disabled' | 'unavailable' | 'ready' | 'quota_exhausted' | 'busy'
  composerMaxGoalCharacters?: number
  collectionCapabilities?: {
    id: string
    label: string
    description: string
    canWrite: boolean
    isDisposable: boolean
  } | null
}

export interface AuthState {
  /** True while the initial `/api/session` check is in flight. */
  isLoading: boolean
  /** True after the API accepts authentication, or returns an explicit policy denial. */
  isAuthenticated: boolean
  /** True only when the server confirms current directory and scope admission. */
  isAuthorized: boolean
  displayName: string | null
  publicDemo?: boolean
  composerEnabled?: boolean
  composerState?: SessionResponse['composerState']
  composerMaxGoalCharacters?: number
  collectionCapabilities?: SessionResponse['collectionCapabilities']
  error: string | null
  signIn: () => Promise<void>
  signOut: () => Promise<void>
  refreshPublicSession?: () => Promise<void>
}

export const AuthContext = createContext<AuthState | undefined>(undefined)
