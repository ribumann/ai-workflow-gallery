import { createElement, type ReactElement, type ReactNode } from 'react'
import { render, type RenderOptions } from '@testing-library/react'
import { AuthContext, type AuthState } from '../../src/auth/authContext'

const defaultAuthState: AuthState = {
  isLoading: false,
  isAuthenticated: true,
  isAuthorized: true,
  displayName: 'Test user',
  publicDemo: false,
  composerEnabled: false,
  collectionCapabilities: null,
  error: null,
  signIn: async () => {},
  signOut: async () => {},
}

type AuthRenderOptions = RenderOptions & { authState?: Partial<AuthState> }

export function renderWithAuth(ui: ReactElement, options: AuthRenderOptions = {}) {
  const { authState = {}, wrapper: ExistingWrapper, ...renderOptions } = options
  const Wrapper = ({ children }: { children: ReactNode }) => {
    const content = ExistingWrapper ? createElement(ExistingWrapper, null, children) : children
    return <AuthContext.Provider value={{ ...defaultAuthState, ...authState }}>{content}</AuthContext.Provider>
  }
  return render(ui, { ...renderOptions, wrapper: Wrapper })
}
