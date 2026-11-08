#!/usr/bin/env node
/**
 * Builds the downloadable integration packages from `integrations/` and writes them into
 * `frontend/public/downloads/`, so the Tool integration guides can offer them from the same origin
 * as the public static application shell. They contain instructions, never gallery data or tokens.
 *
 * Runs automatically as the frontend's `prebuild` step, so the published downloads can never drift
 * from the skill source in the repository.
 *
 * Produces:
 *   - ai-workflow-gallery-cowork-plugin.zip  — M365 app package, for tenant-wide Cowork deployment
 *   - ai-workflow-gallery-skill.zip          — the bare skill folder, for GitHub Copilot and Scout
 *   - ai-workflow-gallery-SKILL.md           — the single skill file, for per-user upload paths
 *   - ai-workflow-gallery-<reference>.md     — each reference file, for the same upload paths
 *
 * The reference files are published individually because the single-file upload path (Cowork's
 * Option A) would otherwise install a skill whose own "Additional resources" links resolve to
 * nothing: `references/` only ever existed inside the two archives.
 *
 * All three share one skill source, `integrations/skills/ai-workflow-gallery/`. The Cowork package
 * is deliberately **skills-only**: it declares no `agentConnectors`, because Cowork connectors can
 * only authenticate with OAuth, an API key or Dynamic Client Registration, and the gallery API
 * requires an explicitly registered delegated Entra OAuth client. Cloud-client compatibility
 * must be verified separately. The skill uses the gallery's MCP tools where they are
 * available and otherwise drives the website through local browser use, which reuses the user's
 * existing sign-in. See `integrations/README.md`.
 */

import { createHash } from 'node:crypto'
import { deflateSync } from 'node:zlib'
import { mkdir, readFile, readdir, writeFile, rm } from 'node:fs/promises'
import { dirname, join, posix, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import JSZip from 'jszip'
import { brandingText, loadBranding } from './branding.mjs'

const branding = loadBranding()

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
const integrationsRoot = join(repoRoot, 'integrations')
const skillDir = join(integrationsRoot, 'skills', 'ai-workflow-gallery')
const referenceDir = join(skillDir, 'references')
const manifestPath = join(integrationsRoot, 'cowork-plugin', 'manifest.json')
const outputDir = join(repoRoot, 'frontend', 'public', 'downloads')

const coworkZipName = 'ai-workflow-gallery-cowork-plugin.zip'
const skillZipName = 'ai-workflow-gallery-skill.zip'
const skillFileName = 'ai-workflow-gallery-SKILL.md'
const referencePrefix = 'ai-workflow-gallery-'

/** Fixed timestamp: identical sources must produce byte-identical archives, so that a rebuild does
 * not show up as a spurious change in the deployed frontend. */
const epoch = new Date('2020-01-01T00:00:00Z')

/** Fluent `colorBrandBackground`, matching the gallery's own accent. */
const brand = (branding.accent || '#0F6CBD').match(/[0-9a-f]{2}/giu).map((part) => parseInt(part, 16))

function brandedText(buffer) {
  return brandingText(branding, buffer.toString('utf8'))
}

// --- Minimal PNG encoder -----------------------------------------------------------------------
// Icons are generated rather than committed, so there are no binaries in the repository and the
// accent colour has a single source of truth. PNG is simple enough to emit directly: signature,
// IHDR, one deflated IDAT of filter-0 scanlines, IEND.

const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n
  for (let k = 0; k < 8; k += 1) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
  return c >>> 0
})

function crc32(buffer) {
  let c = 0xffffffff
  for (const byte of buffer) c = crcTable[(c ^ byte) & 0xff] ^ (c >>> 8)
  return (c ^ 0xffffffff) >>> 0
}

function chunk(type, data) {
  const length = Buffer.alloc(4)
  length.writeUInt32BE(data.length)
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data])
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(body))
  return Buffer.concat([length, body, crc])
}

/** @param {number} size @param {(x: number, y: number) => [number, number, number, number]} shade */
function png(size, shade) {
  const raw = Buffer.alloc(size * (size * 4 + 1))
  let offset = 0
  for (let y = 0; y < size; y += 1) {
    raw[offset] = 0 // filter: none
    offset += 1
    for (let x = 0; x < size; x += 1) {
      const [r, g, b, a] = shade(x, y)
      raw[offset] = r
      raw[offset + 1] = g
      raw[offset + 2] = b
      raw[offset + 3] = a
      offset += 4
    }
  }
  const ihdr = Buffer.alloc(13)
  ihdr.writeUInt32BE(size, 0)
  ihdr.writeUInt32BE(size, 4)
  ihdr[8] = 8 // bit depth
  ihdr[9] = 6 // colour type: RGBA
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ])
}

/**
 * A 2×2 grid of rounded tiles — a "gallery" of entries. Reads as four distinct blocks at 192px
 * and still resolves as a grid at 32px.
 */
function isTile(x, y, size) {
  const unit = size / 16
  const cell = (v) => {
    const half = v < size / 2 ? v : v - size / 2
    return half >= unit * 1.5 && half <= unit * 6.5
  }
  if (!cell(x) || !cell(y)) return false
  // Knock the sharp corners off each tile so it reads as rounded at large sizes.
  const cx = (x % (size / 2)) - size / 4
  const cy = (y % (size / 2)) - size / 4
  const corner = Math.abs(cx) > unit * 2 && Math.abs(cy) > unit * 2
  return !(corner && Math.hypot(Math.abs(cx) - unit * 2, Math.abs(cy) - unit * 2) > unit * 2.5)
}

function colorIcon(size) {
  return png(size, (x, y) =>
    isTile(x, y, size) ? [255, 255, 255, 255] : [brand[0], brand[1], brand[2], 255],
  )
}

function outlineIcon(size) {
  // Outline icons are a single colour on transparency.
  return png(size, (x, y) => (isTile(x, y, size) ? [255, 255, 255, 255] : [0, 0, 0, 0]))
}

// --- Packaging ---------------------------------------------------------------------------------

async function addDirectory(zip, absoluteDir, zipDir) {
  // Register every folder segment explicitly, with the fixed date. JSZip creates implicit folder
  // entries stamped with the current time, which alone changes the archive on every build.
  let prefix = ''
  for (const segment of zipDir.split('/').filter(Boolean)) {
    prefix = posix.join(prefix, segment)
    zip.file(prefix, null, { dir: true, date: epoch })
  }
  const entries = await readdir(absoluteDir, { withFileTypes: true })
  // Sorted, so archive entry order is stable across platforms.
  entries.sort((a, b) => a.name.localeCompare(b.name))
  for (const entry of entries) {
    const absolute = join(absoluteDir, entry.name)
    const inZip = posix.join(zipDir, entry.name)
    if (entry.isDirectory()) {
      await addDirectory(zip, absolute, inZip)
    } else {
      const content = await readFile(absolute)
      zip.file(inZip, entry.name.endsWith('.md') ? brandedText(content) : content, { date: epoch })
    }
  }
}

function generate(zip) {
  return zip.generateAsync({
    type: 'nodebuffer',
    compression: 'DEFLATE',
    compressionOptions: { level: 9 },
    platform: 'UNIX',
  })
}

function describe(name, buffer) {
  const digest = createHash('sha256').update(buffer).digest('hex').slice(0, 12)
  return `${name} (${(buffer.length / 1024).toFixed(1)} kB, sha256 ${digest})`
}

async function main() {
  await mkdir(outputDir, { recursive: true })
  const built = []

  // 1. Cowork: manifest + icons + the skill folder, as an M365 app package.
  if (branding.publisher) {
    const manifest = JSON.parse(await readFile(manifestPath, 'utf8'))
    manifest.developer = {
      name: branding.publisher.name,
      websiteUrl: branding.publisher.website,
      privacyUrl: branding.publisher.privacy,
      termsOfUseUrl: branding.publisher.terms,
    }
    manifest.name = { short: branding.title, full: `${branding.title} for Copilot Cowork` }
    manifest.description.full = brandedText(Buffer.from(manifest.description.full))
    manifest.accentColor = branding.accent || '#0F6CBD'
    const coworkZip = new JSZip()
    coworkZip.file('manifest.json', JSON.stringify(manifest, null, 2), { date: epoch })
    coworkZip.file('color.png', colorIcon(192), { date: epoch })
    coworkZip.file('outline.png', outlineIcon(32), { date: epoch })
    await addDirectory(coworkZip, skillDir, 'skills/ai-workflow-gallery')
    const coworkBuffer = await generate(coworkZip)
    await writeFile(join(outputDir, coworkZipName), coworkBuffer)
    built.push(describe(coworkZipName, coworkBuffer))
  } else {
    await rm(join(outputDir, coworkZipName), { force: true })
    console.log('Cowork publication not configured: no plugin ZIP emitted. Configure branding.publisher with real publisher and legal URLs.')
  }

  // 2. GitHub Copilot and Scout: the skill folder on its own, ready to drop into a skills
  //    directory. The top-level folder name must match the skill's frontmatter `name`.
  const skillZip = new JSZip()
  await addDirectory(skillZip, skillDir, 'ai-workflow-gallery')
  const skillBuffer = await generate(skillZip)
  await writeFile(join(outputDir, skillZipName), skillBuffer)
  built.push(describe(skillZipName, skillBuffer))

  // 3. The bare skill file, for surfaces that take a single upload.
  await writeFile(join(outputDir, skillFileName), brandedText(await readFile(join(skillDir, 'SKILL.md'))))
  built.push(skillFileName)

  // 4. Each reference file, under the same naming convention. A single-file upload has no way to
  //    carry `references/`, so publishing them separately is what keeps the links in SKILL.md's
  //    "Additional resources" from dangling for anyone who installed it that way.
  const referenceNames = (await readdir(referenceDir)).filter((n) => n.endsWith('.md')).sort()
  for (const name of referenceNames) {
    const published = `${referencePrefix}${name}`
    await writeFile(join(outputDir, published), brandedText(await readFile(join(referenceDir, name))))
    built.push(published)
  }

  const shown = relative(repoRoot, outputDir).replace(/\\/g, '/')
  console.log(`Built into ${shown}/\n  ${built.join('\n  ')}`)
}

await main()
