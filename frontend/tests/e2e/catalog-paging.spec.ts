import { test, expect } from '@playwright/test'

test('traverses the complete paged catalog without omissions or duplicates', async ({
  page,
}, testInfo) => {
  const timestamp = '2026-01-01T00:00:00Z'
  const tasks = Array.from({ length: 225 }, (_, index) => ({
    id: `task-${index + 1}`,
    name: `Task ${index + 1}`,
    description: 'Catalog paging fixture.',
    taskType: 'prompt',
    targetIds: ['github.copilot.vscode'],
    tags: [],
    hasReferenceLink: true,
    hasInlineContent: false,
    hasAttachedFile: false,
    format: null,
    setup: null,
    prerequisites: null,
    createdByDisplayName: 'Test contributor',
    lastEditedAt: timestamp,
  }))
  const workflows = Array.from({ length: 37 }, (_, index) => ({
    id: `workflow-${index + 1}`,
    name: `Workflow ${index + 1}`,
    problemStatement: 'Catalog paging fixture.',
    desiredOutcome: 'Complete traversal.',
    stepCount: 2,
    targetIds: ['github.copilot.vscode'],
    createdByDisplayName: 'Test contributor',
    lastEditedAt: timestamp,
  }))
  const methods = Array.from({ length: 15 }, (_, index) => ({
    id: `method-${index + 1}`,
    title: `Method ${index + 1}`,
    summary: 'Catalog paging fixture.',
    createdByDisplayName: 'Test contributor',
    lastEditedAt: timestamp,
  }))
  await page.route('**/api/session?*', (route) =>
    route.fulfill({
      json: { isAuthenticated: true, isAuthorized: true, displayName: 'Test User' },
    }),
  )
  await page.route(/\/api\/(tasks|workflows|methods)\?/, (route) => {
    const url = new URL(route.request().url())
    const number = Number(url.searchParams.get('page'))
    const size = Number(url.searchParams.get('pageSize'))
    expect(size).toBe(25)
    const all = url.pathname.endsWith('/tasks')
      ? tasks
      : url.pathname.endsWith('/workflows')
        ? workflows
        : methods
    return route.fulfill({
      json: {
        items: all.slice((number - 1) * size, number * size),
        totalCount: all.length,
        page: number,
        pageSize: size,
      },
    })
  })
  await page.goto('/?collectionId=public-techniques')
  const nav = page.getByRole('navigation', { name: 'Catalog pagination' })
  const seen: string[] = []
  for (let number = 1; number <= 9; number++) {
    await expect(nav.getByText(`Page ${number} of 9`, { exact: true })).toBeVisible()
    await expect(
      nav.getByText(`${(number - 1) * 25 + 1}–${number * 25} of 225 items`, { exact: true }),
    ).toBeVisible()
    const links = page.getByRole('link', { name: /^Task \d+$/ })
    await expect(links).toHaveCount(25)
    seen.push(
      ...(await links.evaluateAll((elements) =>
        elements.map((element) => element.getAttribute('href')!),
      )),
    )
    if (number === 1) {
      await expect(nav.getByRole('button', { name: 'Previous' })).toBeDisabled()
      // One bounded, batched desktop/mobile inspection, produced by the two configured projects.
      await page.screenshot({ path: testInfo.outputPath('catalog-paging.png'), fullPage: true })
      const bounds = await nav.boundingBox()
      expect(bounds!.x).toBeGreaterThanOrEqual(0)
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(page.viewportSize()!.width)
    }
    if (number < 9) await nav.getByRole('button', { name: 'Next', exact: true }).click()
  }
  expect(new Set(seen).size).toBe(225)
  expect(seen).toEqual(tasks.map((task) => `/tasks/${task.id}`))
  await expect(nav.getByRole('button', { name: 'Next', exact: true })).toBeDisabled()
  await page.reload()
  await expect(nav.getByText('Page 9 of 9', { exact: true })).toBeVisible()
  await page.goBack()
  await expect(nav.getByText('Page 8 of 9', { exact: true })).toBeVisible()
  await page.getByRole('tab', { name: 'Workflows', exact: true }).click()
  await expect(nav.getByText('Page 1 of 2', { exact: true })).toBeVisible()
  await expect(nav.getByText('1–25 of 37 items', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: /^Workflow \d+$/ })).toHaveCount(25)
  await nav.getByRole('button', { name: 'Next', exact: true }).click()
  await expect(nav.getByText('26–37 of 37 items', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: /^Workflow \d+$/ })).toHaveCount(12)
  await expect(nav.getByRole('button', { name: 'Next', exact: true })).toBeDisabled()
  await page.getByRole('tab', { name: 'Methods', exact: true }).click()
  await expect(nav.getByText('1–15 of 15 items', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: /^Method \d+$/ })).toHaveCount(15)
  await expect(nav.getByRole('button', { name: 'Previous' })).toBeDisabled()
  await expect(nav.getByRole('button', { name: 'Next', exact: true })).toBeDisabled()
})
