import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'

const idPattern = /^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$/
const roles = new Set(['execution', 'context', 'output', 'builder', 'governance', 'access-program'])

export function validateRegistry(registry) {
  const fail = (message) => { throw new Error(`Catalog registry: ${message}`) }
  if (registry?.version !== 1) fail('unsupported version')
  const ids = (items, name) => {
    if (!Array.isArray(items) || !items.length || items.length > 200) fail(`${name} must be a bounded nonempty list`)
    const seen = new Set()
    for (const item of items) {
      if (!idPattern.test(item.id) || typeof item.label !== 'string' || !item.label.trim() || item.label.length > 160)
        fail(`invalid ${name} ID or label`)
      if (seen.has(item.id)) fail(`duplicate ${name} ID ${item.id}`)
      seen.add(item.id)
    }
    return seen
  }
  const kinds = ids(registry.kinds, 'kind')
  const formats = ids(registry.formats, 'format')
  ids(registry.targets, 'target')
  for (const target of registry.targets) {
    if (!roles.has(target.role)) fail(`unknown role on ${target.id}`)
    if (!Array.isArray(target.aliases) || target.aliases.some((alias) => typeof alias !== 'string' || !alias.trim()))
      fail(`invalid aliases on ${target.id}`)
    for (const field of ['provider', 'product', 'host', 'prerequisites']) {
      if (typeof target[field] !== 'string' || !target[field].trim() || target[field].length > 2000)
        fail(`invalid ${field} on ${target.id}`)
    }
    if (!Array.isArray(target.profiles) || target.profiles.length > kinds.size) fail(`invalid profiles on ${target.id}`)
    if (['context', 'output', 'governance', 'access-program'].includes(target.role) && target.profiles.length)
      fail(`${target.role} is not an artifact execution profile`)
    const seen = new Set()
    for (const profile of target.profiles) {
      if (!kinds.has(profile.kind) || seen.has(profile.kind)) fail(`unknown or duplicate kind on ${target.id}`)
      seen.add(profile.kind)
      if (!Array.isArray(profile.formats) || !profile.formats.length ||
          new Set(profile.formats).size !== profile.formats.length || profile.formats.some((id) => !formats.has(id)))
        fail(`unknown or duplicate format on ${target.id}`)
      let url
      try { url = new URL(profile.sourceUrl) } catch { fail(`invalid source on ${target.id}`) }
      if (url.protocol !== 'https:' || url.username || url.password) fail(`public HTTPS evidence required on ${target.id}`)
      if (!/^\d{4}-\d{2}-\d{2}$/.test(profile.reviewedOn) ||
          new Date(profile.reviewedOn).toISOString().slice(0, 10) !== profile.reviewedOn)
        fail(`invalid review date on ${target.id}`)
      if (typeof profile.note !== 'string' || !profile.note.trim()) fail(`missing evidence note on ${target.id}`)
    }
  }
  return registry
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const registry = validateRegistry(JSON.parse(readFileSync(new URL('../catalog/registry.json', import.meta.url), 'utf8')))
  console.log(`Catalog registry valid: ${registry.kinds.length} kinds, ${registry.formats.length} formats, ${registry.targets.length} targets.`)
}
