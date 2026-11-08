const STORAGE_KEY = 'workflow-gallery-device-id'

/**
 * Returns a stable per-browser identifier used to scope one-vote-per-device rating (issue #60).
 * Generated once and persisted in `localStorage`; not a security boundary — a determined user can
 * clear storage and vote again, which is an accepted limitation for an internal, informational
 * engagement signal.
 */
export function getOrCreateDeviceId(): string {
  try {
    const existing = window.localStorage.getItem(STORAGE_KEY)
    if (existing) {
      return existing
    }
    const generated = crypto.randomUUID()
    window.localStorage.setItem(STORAGE_KEY, generated)
    return generated
  } catch {
    // Storage unavailable (e.g. private browsing with storage disabled) — fall back to a
    // per-call value so rating still works, it just won't persist a vote across reloads.
    return crypto.randomUUID()
  }
}
