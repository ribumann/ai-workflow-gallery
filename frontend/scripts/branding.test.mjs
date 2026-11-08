import { strict as assert } from 'node:assert'
import { test } from 'node:test'
import { execFileSync } from 'node:child_process'
import { readFileSync, existsSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { resolve, dirname, basename } from 'node:path'
import { tmpdir } from 'node:os'
import JSZip from 'jszip'
import { brandingHtml, brandingText, validateBranding } from './branding.mjs'
import { loadCollectionBranding } from './collection-branding.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const sample = JSON.parse(readFileSync(resolve(root, 'branding.example.json')))

test('all three public collections have validated branding, including Playground', () => {
  const presets = loadCollectionBranding()
  assert.deepEqual(Object.keys(presets).sort(), ['curated-tools', 'playground', 'public-techniques'])
  assert.equal(presets.playground.title, 'AI Workflow Playground')
})

test('neutral default, exact public fields and escaped pre-React HTML', () => {
  assert.deepEqual(validateBranding({ title: 'AI Workflow Gallery' }), {
    title: 'AI Workflow Gallery',
    shareChannels: ['copy', 'email', 'teams'],
  })
  assert.equal(
    brandingHtml({ title: 'A & <B>' }, '<title>old</title>'),
    '<title>A &amp; &lt;B&gt;</title>',
  )
  assert.equal(
    brandingHtml({ title: 'Research $& Development' }, '<title>old</title>'),
    '<title>Research $&amp; Development</title>',
  )
  assert.equal(validateBranding(sample).publisher.name, 'Example Organization')
})

test('bad types, unknown fields, URLs, assets, colors and incomplete publishers fail loudly', () => {
  for (const invalid of [
    { title: '' },
    { title: 12 },
    { organization: [] },
    { feedbackRecipient: 'private' },
    { tenantId: 'private' },
    { links: { support: 'javascript:alert(1)' } },
    { links: { source: 'https://user:password@example.org' } },
    { links: { terms: '/terms' } },
    { links: { arbitrary: 'https://example.org' } },
    { logo: 'https://example.org/logo.svg' },
    { logo: '/../branding.json' },
    { favicon: '/missing.png' },
    { logo: 4 },
    { accent: '#fff' },
    { accent: '#FFFFFF' },
    { shareChannels: ['unknown'] },
    { shareChannels: ['copy', 'copy'] },
    { shareChannels: [] },
    { publisher: { name: 'Example' } },
    { publisher: { ...sample.publisher, secret: 'private' } },
  ]) {
    assert.throws(() => validateBranding({ title: 'AI Workflow Gallery', ...invalid }))
  }
})

test('configured titles and publisher names are literal, single-pass and safely quoted in YAML', () => {
  for (const title of [
    'Sample AI Workflow Gallery',
    'The "AI Workflow Gallery"',
    'Research $& Development',
    "Research $` $' $$",
  ]) {
    const config = validateBranding({ title })
    const rendered = brandingText(config, '# AI Workflow Gallery\n  author: AI Workflow Gallery')
    assert.equal(rendered, `# ${title}\n  author: ${JSON.stringify(title)}`)
  }
  const config = validateBranding({
    title: 'Research $&',
    publisher: { ...sample.publisher, name: 'AI Workflow Gallery "Research"' },
  })
  assert.equal(
    brandingText(config, '  author: AI Workflow Gallery'),
    `  author: ${JSON.stringify(config.publisher.name)}`,
  )
  const skill = readFileSync(
    resolve(root, '..', 'integrations', 'skills', 'ai-workflow-gallery', 'SKILL.md'),
    'utf8',
  )
  const longest = brandingText(validateBranding({ title: 'X'.repeat(80) }), skill)
  const description = longest.match(/description: \|\r?\n([\s\S]*?)\r?\nlicense:/u)[1]
  assert.ok(description.replace(/^  /gmu, '').replace(/\r?\n/gu, ' ').length < 800)
})

test('malformed and externally loaded SVG assets fail rather than publishing broken branding', () => {
  const dir = mkdtempSync(resolve(root, 'public', 'branding-test-'))
  const path = resolve(dir, 'test.svg')
  try {
    for (const content of [
      '<svg',
      '<svg xmlns="http://www.w3.org/2000/svg"><path></svg>',
      '<html/>',
      '<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>',
      '<svg xmlns="http://www.w3.org/2000/svg"><image href="https://example.org/a.svg"/></svg>',
    ]) {
      writeFileSync(path, content)
      assert.throws(() =>
        validateBranding({ title: 'Gallery', logo: `/${basename(dir)}/test.svg` }),
      )
    }
  } finally {
    rmSync(dir, { recursive: true })
  }
})

test('default and sample archives are neutral, deterministic and preserve identities and paths', async () => {
  const fixture = mkdtempSync(resolve(tmpdir(), 'gallery-branding-'))
  const neutral = resolve(fixture, 'neutral.json')
  writeFileSync(neutral, JSON.stringify({ title: 'AI Workflow Gallery' }))
  const build = (file) =>
    execFileSync(process.execPath, ['scripts/build-integration-downloads.mjs'], {
      cwd: root,
      env: { ...process.env, GALLERY_BRANDING_FILE: file },
      stdio: 'pipe',
    })
  const output = (file) => readFileSync(resolve(root, 'public', 'downloads', file))
  const inspect = async (name, title) => {
    const zip = await JSZip.loadAsync(output(name))
    for (const [path, entry] of Object.entries(zip.files)) {
      if (entry.dir || path.endsWith('.png')) continue
      const content = await entry.async('string')
      assert.doesNotMatch(content, /FEEDBACK_RECIPIENT|GH_TOKEN/u)
      if (path.endsWith('SKILL.md')) {
        assert.ok(content.includes(`# ${title}`))
        assert.match(content, /name: ai-workflow-gallery/u)
        assert.match(content, /mcp_disabled/u)
      }
      if (path.endsWith('SKILL.md') || path.endsWith('references/mcp-tools.md')) {
        assert.match(content, /--anonymous --collection-id public-techniques/u)
        assert.match(content, /curated-tools/u)
        assert.match(content, /collectionId/u)
        assert.match(content, /EnableMcpWrites=true/u)
      }
      if (path.endsWith('references/mcp-tools.md')) {
        assert.match(content, /PublicMcpAllowedOrigins/u)
        assert.match(content, /per worker/u)
        assert.match(content, /Retry-After/u)
      }
    }
    return zip
  }
  try {
    build('branding.example.json')
    const bytes = output('ai-workflow-gallery-cowork-plugin.zip')
    const zip = await inspect('ai-workflow-gallery-cowork-plugin.zip', sample.title)
    const manifest = JSON.parse(await zip.file('manifest.json').async('string'))
    assert.equal(
      manifest.$schema,
      'https://developer.microsoft.com/json-schemas/teams/v1.28/MicrosoftTeams.schema.json',
    )
    assert.equal(manifest.id, '8f3a9c14-5d2b-4e71-9a6c-2f7b0d4e13aa')
    assert.equal(manifest.manifestVersion, '1.28')
    assert.deepEqual(manifest.name, {
      short: sample.title,
      full: `${sample.title} for Copilot Cowork`,
    })
    assert.equal(manifest.developer.name, sample.publisher.name)
    assert.equal(manifest.developer.privacyUrl, sample.publisher.privacy)
    assert.equal(manifest.developer.termsOfUseUrl, sample.publisher.terms)
    assert.equal(manifest.accentColor, sample.accent)
    assert.equal(manifest.agentConnectors, undefined)
    const icon = await zip.file('color.png').async('nodebuffer')
    assert.equal(icon.readUInt32BE(16), 192)
    assert.equal(icon.readUInt32BE(20), 192)
    await inspect('ai-workflow-gallery-skill.zip', sample.title)
    const skillBytes = output('ai-workflow-gallery-skill.zip')
    const referenceBytes = output('ai-workflow-gallery-mcp-tools.md')
    assert.ok(output('ai-workflow-gallery-SKILL.md').toString().includes(sample.publisher.name))
    build('branding.example.json')
    assert.deepEqual(output('ai-workflow-gallery-cowork-plugin.zip'), bytes)
    assert.deepEqual(output('ai-workflow-gallery-skill.zip'), skillBytes)
    assert.deepEqual(output('ai-workflow-gallery-mcp-tools.md'), referenceBytes)
    build(neutral)
    assert.equal(
      existsSync(resolve(root, 'public/downloads/ai-workflow-gallery-cowork-plugin.zip')),
      false,
    )
    await inspect('ai-workflow-gallery-skill.zip', 'AI Workflow Gallery')
    for (const name of ['SKILL', 'gallery-map', 'mcp-tools']) {
      assert.doesNotMatch(
        output(`ai-workflow-gallery-${name}.md`).toString(),
        /FEEDBACK_RECIPIENT|GH_TOKEN/u,
      )
    }
    const skillZip = await JSZip.loadAsync(output('ai-workflow-gallery-skill.zip'))
    assert.equal(
      await skillZip.file('ai-workflow-gallery/references/mcp-tools.md').async('string'),
      output('ai-workflow-gallery-mcp-tools.md').toString(),
    )
  } finally {
    try {
      build(process.env.GALLERY_BRANDING_FILE || 'branding.json')
    } finally {
      rmSync(fixture, { recursive: true })
    }
  }
})
