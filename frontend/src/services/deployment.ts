const configured = import.meta.env.VITE_PUBLIC_DEMO_ENABLED
if (configured !== undefined && configured !== '' && configured !== 'true' && configured !== 'false')
  throw new Error('VITE_PUBLIC_DEMO_ENABLED must be true or false.')

export const publicDemo = configured === 'true'

export const COLLECTIONS = [
  { id: 'public-techniques', label: 'Public techniques', description: 'Original examples based on public documentation.' },
  { id: 'curated-tools', label: 'Curated tools', description: 'Reviewed, publication-safe content only. No internal tools auto-publish.' },
  { id: 'playground', label: 'Playground', description: 'Sandbox. Never store sensitive or private data.' },
]

export function canWriteCollection(
  publicSession: boolean | undefined,
  capabilities: { id: string; canWrite: boolean } | null | undefined,
): boolean {
  const isPublicMode = publicDemo || publicSession === true
  if (capabilities != null)
    return capabilities.id === selectedCollection() && capabilities.canWrite
  return !isPublicMode
}

export function selectedCollection(): string {
  const requested = new URLSearchParams(window.location.search).get('collectionId')
  const saved = window.sessionStorage.getItem('gallery.collection')
  const id = requested ?? saved ?? 'public-techniques'
  if (!COLLECTIONS.some((collection) => collection.id === id))
    throw new Error('Unknown collection. Open the gallery with a valid collectionId.')
  if (requested) window.sessionStorage.setItem('gallery.collection', requested)
  return id
}

export function scopedPath(path: string): string {
  if (!/^\/api\/(session|catalog|tasks|workflows|methods|reports|ai\/compose)(?:\/|\?|$)/.test(path)) return path
  const url = new URL(path, window.location.origin)
  url.searchParams.set('collectionId', selectedCollection())
  return `${url.pathname}${url.search}`
}
