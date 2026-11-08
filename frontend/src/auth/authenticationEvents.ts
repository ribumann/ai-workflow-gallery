const requiredEvent = 'gallery-reauthentication-required'

export function requestReauthentication() {
  window.dispatchEvent(new Event(requiredEvent))
}

export function onReauthenticationRequired(listener: () => void) {
  window.addEventListener(requiredEvent, listener)
  return () => window.removeEventListener(requiredEvent, listener)
}
