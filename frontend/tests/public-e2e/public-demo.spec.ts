import { test, expect, type Page } from '@playwright/test'
import { method } from '../unit/methodFixtures'
import type { MethodDetail } from '../../src/services/types'

const id = '11111111-1111-4111-8111-111111111111'
const task = {
  id,
  name: 'Review a public source',
  description: 'Separate published facts from assumptions.',
  taskType: 'prompt',
  targetIds: ['microsoft.copilot.chat'],
  format: 'text.plain',
  setup: null,
  prerequisites: null,
  tags: ['public'],
  referenceLink: null,
  inlineContent: 'List the claims and their public evidence. State uncertainty.',
  dataSource: null,
  dataOutput: 'A human-reviewed summary',
  attachedFile: null,
  createdBy: { id: 'publisher', displayName: 'Catalog publisher' },
  createdAt: '2026-11-05T00:00:00Z',
  lastEditedBy: { id: 'publisher', displayName: 'Catalog publisher' },
  lastEditedAt: '2026-11-05T00:00:00Z',
  referencedByWorkflowIds: [],
}

async function mockPublic(
  page: Page,
  mode: 'ready' | 'error' | 'empty' = 'ready',
  composerState: 'disabled' | 'ready' | 'unavailable' | 'quota_exhausted' | 'busy' = 'disabled',
) {
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    expect(request.headers().authorization).toBeUndefined()
    if (url.pathname === '/api/ai/compose') {
      expect(composerState).toBe('ready')
      expect(request.method()).toBe('POST')
      expect(url.searchParams.get('collectionId')).toMatch(
        /^(public-techniques|curated-tools|playground)$/,
      )
      expect(request.postDataJSON()).toEqual({ goal: 'Review evidence' })
      await route.fulfill({
        json: {
          suggestedName: 'Evidence proposal',
          problemStatement: 'Review evidence',
          suggestedOutcome: 'A reviewed summary',
          steps: [
            {
              sequenceOrder: 1,
              taskId: id,
              taskName: task.name,
              taskType: task.taskType,
              targetId: task.targetIds[0],
              notes: null,
            },
          ],
          gaps: [
            {
              description: 'Human approval',
              sequenceHint: 2,
              suggestedTaskName: 'Approval',
              suggestedTaskDescription: 'Review the evidence',
            },
          ],
        },
      })
      return
    }
    expect(request.method()).toBe('GET')
    if (url.pathname === '/api/session') {
      await route.fulfill({
        json: {
          isAuthenticated: false,
          isAuthorized: false,
          displayName: null,
          publicDemo: true,
          composerEnabled: composerState === 'ready',
          composerState,
          composerMaxGoalCharacters: 1000,
          collectionCapabilities: {
            id: url.searchParams.get('collectionId'),
            label: 'Selected collection',
            description: 'Public collection',
            canWrite: url.searchParams.get('collectionId') === 'playground',
            isDisposable: url.searchParams.get('collectionId') === 'playground',
          },
        },
      })
      return
    }
    expect(url.searchParams.get('collectionId')).toMatch(
      /^(public-techniques|curated-tools|playground)$/,
    )
    if (url.pathname === `/api/tasks/${id}`) {
      await route.fulfill({ json: task })
      return
    }
    if (mode === 'error') {
      await route.fulfill({ status: 503, json: { message: 'Unavailable' } })
      return
    }
    const items =
      mode === 'empty' || url.searchParams.get('collectionId') === 'curated-tools' ? [] : [task]
    await route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 50 } })
  })
}

test('anonymous discovery and detail never expose publishing or paid-agent actions', async ({
  page,
}, testInfo) => {
  await mockPublic(page)
  await page.goto('/')
  await expect(page.getByRole('heading', { name: /Discover tasks/ })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Selected collection' })).toContainText(
    'Published content is read-only.',
  )
  await expect(page.getByRole('region', { name: 'Selected collection' })).not.toContainText(
    'Original examples based on public documentation.',
  )
  await expect(page.getByRole('region', { name: 'Selected collection' })).not.toHaveText(
    /Composer|anonymous visitors/,
  )
  await expect(page.getByRole('button', { name: 'New task', exact: true })).toHaveCount(0)
  await expect(
    page.getByRole('button', { name: 'Workflow composer unavailable', exact: true }),
  ).toBeDisabled()
  await expect(page.getByRole('button', { name: /give feedback/i })).toHaveCount(0)
  await page.screenshot({ path: testInfo.outputPath('catalog.png'), fullPage: true })
  await page.getByRole('link', { name: task.name, exact: true }).click()
  await expect(page.getByRole('heading', { name: task.name })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Edit', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Delete', exact: true })).toHaveCount(0)
  await expect(page.getByText('Comments', { exact: true })).toHaveCount(0)
  const outputHeading = page.getByRole('heading', { name: 'Data output', exact: true })
  await expect(outputHeading).toHaveCSS('display', 'block')
  const headingBox = await outputHeading.boundingBox()
  const valueBox = await page.getByText(task.dataOutput, { exact: true }).boundingBox()
  expect(headingBox).not.toBeNull()
  expect(valueBox).not.toBeNull()
  expect(valueBox!.y).toBeGreaterThanOrEqual(headingBox!.y + headingBox!.height)
  await page.getByText('Compatibility, setup & access', { exact: true }).click()
  await expect(page.getByText(/not a verified gallery connection/)).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({ path: testInfo.outputPath('task-detail.png'), fullPage: true })
})

test('collection selection is keyboard reachable and curated collection is genuinely empty', async ({
  page,
}) => {
  await mockPublic(page)
  await page.goto('/')
  const picker = page.getByRole('combobox', { name: 'Collection', exact: true })
  await picker.focus()
  await page.keyboard.press('ArrowDown')
  await page.getByRole('option', { name: 'Curated tools', exact: true }).click()
  await expect(page).toHaveURL(/collectionId=curated-tools/)
  await expect(page.getByText('The catalog is empty', { exact: true })).toBeVisible()
  await expect(
    page.getByText(
      'No published tasks are available in this collection. Choose another collection to browse.',
      { exact: true },
    ),
  ).toBeVisible()
  await expect(page.getByText(/New task|contribute the first/)).toHaveCount(0)
  await expect(page.getByText(/No internal tools auto-publish/)).toBeVisible()
  await expect(page.getByRole('region', { name: 'Selected collection' })).not.toHaveText(
    /Composer|anonymous visitors/,
  )
})

test('failed reads show an error instead of an empty published collection', async ({ page }) => {
  await mockPublic(page, 'error')
  await page.goto('/')
  await expect(page.getByText(/Could not load the catalog/)).toBeVisible()
  await expect(page.getByText('The catalog is empty', { exact: true })).toHaveCount(0)
})

test('anonymous authoring routes return to read-only discovery', async ({ page }) => {
  await mockPublic(page, 'empty')
  await page.goto('/tasks/new')
  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { name: /Discover tasks/ })).toBeVisible()
})

test('explicitly ready anonymous Composer makes a scoped proposal without enabling writes', async ({
  page,
}) => {
  await mockPublic(page, 'ready', 'ready')
  for (const collectionId of ['public-techniques', 'curated-tools', 'playground']) {
    await page.goto(`/?collectionId=${collectionId}`)
    await expect(page.getByRole('button', { name: 'Workflow composer', exact: true })).toBeEnabled()
  }

  await page.goto('/?collectionId=playground')
  await expect(page.getByRole('region', { name: 'Selected collection' })).not.toHaveText(
    /Composer|anonymous visitors/,
  )
  await page.getByRole('button', { name: 'Workflow composer', exact: true }).click()
  await page
    .getByRole('textbox', { name: 'What are you trying to achieve?' })
    .fill('Review evidence')
  await page.getByRole('button', { name: 'Get a proposal', exact: true }).click()
  await expect(page.getByText('Evidence proposal', { exact: true })).toBeVisible()
  await expect(page.getByText(/not saved as gallery history/)).toBeVisible()
  await expect(page.getByRole('button', { name: /Review and save workflow|Add task/ })).toHaveCount(
    0,
  )
  await expect(page.getByRole('button', { name: /Sign in|Sign out|give feedback/i })).toHaveCount(0)
})

for (const state of ['unavailable', 'quota_exhausted', 'busy'] as const) {
  test(`server Composer readiness ${state} is not presented as usable`, async ({ page }) => {
    await mockPublic(page, 'ready', state)
    await page.goto('/?tab=workflows')
    await expect(
      page.getByRole('button', { name: /Workflow composer (unavailable|limit reached|busy)/ }),
    ).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Workflow composer', exact: true })).toHaveCount(
      0,
    )
    await expect(page.getByRole('heading', { name: /Discover tasks/ })).toBeVisible()
  })
}

test('transient busy readiness can be explicitly rechecked without paid inference', async ({
  page,
}) => {
  await mockPublic(page, 'ready', 'ready')
  let checks = 0
  let modelCalls = 0
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/ai/compose') modelCalls++
  })
  await page.route('**/api/session?*', async (route) => {
    checks++
    await route.fulfill({
      json: {
        isAuthenticated: false,
        isAuthorized: false,
        displayName: null,
        publicDemo: true,
        composerEnabled: checks > 1,
        composerState: checks > 1 ? 'ready' : 'busy',
        composerMaxGoalCharacters: 1000,
        collectionCapabilities: {
          id: 'public-techniques',
          label: 'Public techniques',
          description: 'Published collection',
          canWrite: false,
          isDisposable: false,
        },
      },
    })
  })
  await page.goto('/?tab=workflows')
  await expect(
    page.getByRole('button', { name: 'Workflow composer busy', exact: true }),
  ).toBeDisabled()
  await page.getByRole('button', { name: 'Recheck workflow composer', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Workflow composer', exact: true })).toBeEnabled()
  expect(checks).toBe(2)
  expect(modelCalls).toBe(0)
})

test('anonymous Playground authoring uses scoped editors and a collection switch removes write access', async ({
  page,
}) => {
  const pageErrors: string[] = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  let saved: MethodDetail | null = null
  const mutations: string[] = []
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const collectionId = url.searchParams.get('collectionId')
    expect(request.headers().authorization).toBeUndefined()
    expect(collectionId).toMatch(/^(public-techniques|curated-tools|playground)$/)
    if (url.pathname === '/api/session') {
      return route.fulfill({
        json: {
          isAuthenticated: false,
          isAuthorized: false,
          displayName: null,
          publicDemo: true,
          composerEnabled: false,
          composerState: 'disabled',
          collectionCapabilities: {
            id: collectionId,
            label: 'Selected collection',
            description: 'Public collection',
            canWrite: collectionId === 'playground',
            isDisposable: collectionId === 'playground',
          },
        },
      })
    }
    if (request.method() !== 'GET') {
      expect(collectionId).toBe('playground')
      expect(url.pathname).toMatch(/^\/api\/methods(?:\/|$)/)
      mutations.push(request.method())
      if (request.method() === 'DELETE') {
        saved = null
        return route.fulfill({ status: 204 })
      }
      const body = request.postDataJSON()
      if (request.method() === 'PUT') expect(body.expectedVersion).toBe(saved?.version)
      saved = {
        ...method,
        ...body,
        version: '22222222-2222-4222-8222-222222222222',
        createdBy: { ...method.createdBy, displayName: 'Playground contributor' },
        lastEditedBy: { ...method.lastEditedBy, displayName: 'Playground contributor' },
      }
      return route.fulfill({ json: saved, status: request.method() === 'POST' ? 201 : 200 })
    }
    if (url.pathname.endsWith('/rating'))
      return route.fulfill({ json: { helpfulCount: 0, notHelpfulCount: 0, myVote: null } })
    if (url.pathname.endsWith('/comments')) return route.fulfill({ json: { items: [] } })
    if (url.pathname === `/api/methods/${method.id}` && saved) return route.fulfill({ json: saved })
    return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 25 } })
  })

  await page.goto('/?collectionId=playground')
  await expect(page.getByRole('region', { name: 'Selected collection' })).toContainText(
    'Anyone can contribute.',
  )
  await expect(page.getByRole('button', { name: 'New task', exact: true })).toBeVisible()
  await page.goto('/methods/new?collectionId=playground')
  await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Sandbox review method')
  await page.getByRole('textbox', { name: 'Short summary' }).fill('Public disposable method')
  await page
    .getByRole('textbox', { name: 'Written approach' })
    .fill('Review evidence before saving.')
  await page.getByRole('button', { name: 'Save method' }).click()
  await expect(page.getByRole('heading', { name: 'Sandbox review method' })).toBeVisible()
  await page.getByRole('button', { name: 'Edit method' }).click()
  await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Updated sandbox method')
  await page.getByRole('button', { name: 'Save method' }).click()
  await expect(page.getByRole('heading', { name: 'Updated sandbox method' })).toBeVisible()
  await page.getByRole('button', { name: 'Delete method' }).click()
  await page.getByRole('button', { name: 'Confirm delete' }).click()
  await expect(page.getByRole('heading', { name: /Discover tasks/ })).toBeVisible()
  expect(mutations).toEqual(['POST', 'PUT', 'DELETE'])

  await page.goto('/methods/new?collectionId=playground')
  await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Unsaved sandbox draft')
  await page.goto('/?collectionId=public-techniques')
  await expect(page.getByRole('heading', { name: /Discover tasks/ })).toBeVisible()
  await expect(page.getByRole('button', { name: 'New task', exact: true })).toHaveCount(0)
  await expect(page.getByRole('textbox', { name: 'Title', exact: true })).toHaveCount(0)
  await page.goto('/methods/new?collectionId=public-techniques')
  await expect(page.getByRole('heading', { name: /Discover tasks/ })).toBeVisible()
  expect(mutations).toEqual(['POST', 'PUT', 'DELETE'])
  expect(pageErrors).toEqual([])
})
