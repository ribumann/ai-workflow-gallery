import '@testing-library/jest-dom/vitest'
import { vi } from 'vitest'

vi.mock('../../src/auth/entraClient', () => ({
  acquireGalleryToken: vi.fn().mockResolvedValue(null),
  signIn: vi.fn(),
  signOut: vi.fn(),
  reauthenticate: vi.fn(),
  SignInRequiredError: class SignInRequiredError extends Error {},
}))

// jsdom doesn't implement ResizeObserver, but Fluent UI's MessageBar (used by CommentsSection's
// "Content policy" banner, among others) requires one to mount. A no-op stub is sufficient since
// tests don't assert on reflow/resize behavior.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class ResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
}
