import { readFileSync, realpathSync } from 'node:fs'
import { dirname, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const publicRoot = realpathSync(resolve(root, 'public'))

function fail(message) {
  throw new Error(`Invalid public branding: ${message}`)
}

function text(value, key, max = 80) {
  if (
    typeof value !== 'string' ||
    !value.trim() ||
    value !== value.trim() ||
    value.length > max ||
    /[\u0000-\u001f\u007f]/u.test(value)
  )
    fail(key)
  return value
}

function keys(value, allowed, key) {
  if (
    !value ||
    typeof value !== 'object' ||
    Array.isArray(value) ||
    Object.keys(value).some((name) => !allowed.includes(name))
  )
    fail(key)
}

function url(value, key) {
  text(value, key, 2048)
  let parsed
  try {
    parsed = new URL(value)
  } catch {
    fail(`${key} must be an absolute HTTPS URL`)
  }
  if (parsed.protocol !== 'https:' || parsed.username || parsed.password)
    fail(`${key} must use HTTPS without credentials`)
  return value
}

function asset(value, key) {
  if (typeof value !== 'string' || !/^\/[a-zA-Z0-9/_-]+\.svg$/u.test(value) || value.includes('//'))
    fail(`${key} must be a local public asset path`)
  const path = realpathSync(resolve(publicRoot, `.${value}`))
  if (!path.startsWith(`${publicRoot}${sep}`)) fail(`${key} must stay inside public`)
  const bytes = readFileSync(path)
  const svg = bytes.toString('utf8')
  if (
    /<script|<foreignObject|<style|@import|url\(\s*["']?(?!#)|\bon\w+\s*=|(?:href|src)\s*=\s*["'](?!#)|<!ENTITY|<!DOCTYPE/iu.test(
      svg,
    )
  )
    fail(`${key} must be a self-contained SVG`)
  let document
  try {
    document = new JSDOM(svg, { contentType: 'image/svg+xml' })
  } catch {
    fail(`${key} must be well-formed SVG`)
  }
  const element = document.window.document.documentElement
  const valid = element.localName === 'svg' && element.namespaceURI === 'http://www.w3.org/2000/svg'
  document.window.close()
  if (!valid) fail(`${key} must be SVG`)
  return value
}

export function validateBranding(input) {
  keys(
    input,
    ['title', 'organization', 'logo', 'favicon', 'accent', 'links', 'shareChannels', 'publisher'],
    'unknown field or invalid object',
  )
  const result = { title: text(input.title, 'title'), shareChannels: ['copy', 'email', 'teams'] }
  if (input.organization !== undefined)
    result.organization = text(input.organization, 'organization')
  for (const key of ['logo', 'favicon'])
    if (input[key] !== undefined) result[key] = asset(input[key], key)
  if (input.accent !== undefined) {
    if (typeof input.accent !== 'string' || !/^#[0-9a-f]{6}$/iu.test(input.accent))
      fail('accent must be #RRGGBB')
    const rgb = input.accent.match(/[0-9a-f]{2}/giu).map((part) => parseInt(part, 16) / 255)
    const linear = rgb.map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4))
    const luminance = linear[0] * 0.2126 + linear[1] * 0.7152 + linear[2] * 0.0722
    if (1.05 / (luminance + 0.05) < 4.5) fail('accent needs at least 4.5:1 contrast with white')
    result.accent = input.accent
  }
  if (input.links !== undefined) {
    keys(input.links, ['support', 'privacy', 'terms', 'source'], 'links')
    result.links = Object.fromEntries(
      Object.entries(input.links).map(([key, value]) => [key, url(value, `links.${key}`)]),
    )
  }
  if (input.shareChannels !== undefined) {
    if (
      !Array.isArray(input.shareChannels) ||
      !input.shareChannels.length ||
      new Set(input.shareChannels).size !== input.shareChannels.length ||
      input.shareChannels.some((v) => !['copy', 'email', 'teams'].includes(v))
    )
      fail('shareChannels')
    result.shareChannels = input.shareChannels
  }
  if (input.publisher !== undefined) {
    keys(input.publisher, ['name', 'website', 'privacy', 'terms'], 'publisher')
    if (result.title.length > 30) fail('Cowork publication requires title at most 30 characters')
    result.publisher = {
      name: text(input.publisher.name, 'publisher.name', 32),
      website: url(input.publisher.website, 'publisher.website'),
      privacy: url(input.publisher.privacy, 'publisher.privacy'),
      terms: url(input.publisher.terms, 'publisher.terms'),
    }
  }
  return result
}

export function loadBranding() {
  return validateBranding(
    JSON.parse(
      readFileSync(resolve(root, process.env.GALLERY_BRANDING_FILE || 'branding.json'), 'utf8'),
    ),
  )
}

export function brandingHtml(branding, html) {
  const escape = (value) =>
    value.replace(
      /[&<>"']/gu,
      (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
    )
  return html
    .replace(/<title>.*?<\/title>/u, () => `<title>${escape(branding.title)}</title>`)
    .replace(
      /<link rel="icon"[^>]*>/u,
      () => `<link rel="icon" href="${branding.favicon || '/favicon.svg'}" />`,
    )
}

export function brandingText(branding, content) {
  return content
    .split('\n')
    .map((line) =>
      line.trim() === 'author: AI Workflow Gallery'
        ? `  author: ${JSON.stringify(branding.publisher?.name || branding.title)}`
        : line.replaceAll('AI Workflow Gallery', () => branding.title),
    )
    .join('\n')
}
