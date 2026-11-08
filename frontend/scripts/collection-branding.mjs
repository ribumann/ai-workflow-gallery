import { readFileSync } from 'node:fs'
import { validateBranding } from './branding.mjs'

export function loadCollectionBranding() {
  const presets = JSON.parse(readFileSync(new URL('../collection-branding.json', import.meta.url), 'utf8'))
  if (Object.keys(presets).sort().join(',') !== 'curated-tools,playground,public-techniques')
    throw new Error('Public demo branding requires exactly the three reviewed collection presets.')
  return Object.fromEntries(Object.entries(presets).map(([id, preset]) => [id, validateBranding(preset)]))
}
