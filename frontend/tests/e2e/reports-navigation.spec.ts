import { test, expect, type Locator, type Page } from '@playwright/test'

const link = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Delivery insights and quarterly business outcomes for regional teams',
  description: 'A shared report for reviewing customer delivery trends, outcomes and next actions.',
  url: 'https://reports.example.org/embed',
  kind: 'Report',
  createdByDisplayName: 'Contributor A',
  lastEditedByDisplayName: 'Contributor B',
  createdAt: '2026-09-10T12:00:00Z',
  lastEditedAt: '2026-09-10T12:00:00Z',
  version: '22222222-2222-2222-2222-222222222222',
}

async function setup(page: Page) {
  let entries = [{ ...link }]
  await page.route('**/api/session?*', (route) =>
    route.fulfill({
      json: { isAuthenticated: true, isAuthorized: true, displayName: 'Test User' },
    }),
  )
  await page.route('**/api/tasks?*', (route) =>
    route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 50 } }),
  )
  await page.route('**/api/workflows?*', (route) =>
    route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 50 } }),
  )
  await page.context().route('https://reports.example.org/**', (route) =>
    route.fulfill({
      contentType: 'text/html',
      body: '<html><body><h1>Sample report content</h1></body></html>',
    }),
  )
  await page.route('**/api/reports**', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const id = url.pathname.split('/')[3]
    if (request.method() === 'DELETE') {
      entries = entries.filter((entry) => entry.id !== id)
      return route.fulfill({ status: 204 })
    }
    if (request.method() === 'POST' || request.method() === 'PUT') {
      const saved = { ...link, ...request.postDataJSON() }
      entries = [saved]
      return route.fulfill({ status: request.method() === 'POST' ? 201 : 200, json: saved })
    }
    if (id) {
      const found = entries.find((entry) => entry.id === id)
      return route.fulfill(found ? { json: found } : { status: 404 })
    }
    const kind = url.searchParams.get('kind')
    const q = (url.searchParams.get('q') ?? '').toLowerCase()
    const filtered = entries.filter(
      (entry) =>
        (!kind || entry.kind === kind) &&
        `${entry.name} ${entry.description}`.toLowerCase().includes(q),
    )
    return route.fulfill({
      json: { items: filtered, totalCount: filtered.length, page: 1, pageSize: 25 },
    })
  })
}

async function showNavigation(page: Page) {
  await expect(page.getByRole('button', { name: 'Toggle navigation' })).toBeVisible()
  if (!(await page.getByRole('navigation', { name: 'Main navigation' }).isVisible())) {
    await page.getByRole('button', { name: 'Toggle navigation' }).click()
  }
  return page.getByRole('navigation', { name: 'Main navigation' })
}

async function openGroup(nav: Locator, name: string) {
  const group = nav.getByRole('button', { name, exact: true })
  if ((await group.getAttribute('aria-expanded')) !== 'true') await group.click()
}

test.beforeEach(async ({ page }) => setup(page))

test('grouped navigation follows URLs and history without overflowing', async ({ page }) => {
  await page.goto('/')
  let nav = await showNavigation(page)
  await expect(nav.getByRole('link', { name: 'Catalog', exact: true })).toHaveAttribute(
    'aria-current',
    'page',
  )
  await expect(nav.getByRole('button', { name: 'Reports & AI Dashboards' })).toHaveAttribute(
    'aria-expanded',
    'false',
  )
  await openGroup(nav, 'Reports & AI Dashboards')
  for (const [group, item] of [['Reports & AI Dashboards', 'All links']]) {
    await expect(async () => {
      const groupBounds = await nav.getByRole('button', { name: group, exact: true }).boundingBox()
      const itemBounds = await nav.getByRole('link', { name: item, exact: true }).boundingBox()
      if (!groupBounds || !itemBounds) throw new Error(`Navigation group ${group} is not visible`)
      expect(itemBounds.x - groupBounds.x).toBeGreaterThanOrEqual(24)
      expect(itemBounds.x + itemBounds.width).toBeLessThanOrEqual(
        groupBounds.x + groupBounds.width + 1,
      )
    }).toPass({ timeout: 5_000 })
  }
  await nav.getByRole('link', { name: 'All links', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Reports & AI Dashboards' })).toBeVisible()
  await expect(page.getByText(link.name)).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({ path: test.info().outputPath('reports-directory.png'), fullPage: true })
  nav = await showNavigation(page)
  await openGroup(nav, 'Reports & AI Dashboards')
  await expect(nav.getByRole('link', { name: 'All links', exact: true })).toHaveAttribute(
    'aria-current',
    'page',
  )
  await nav.getByRole('link', { name: 'Catalog', exact: true }).click()
  await page.getByRole('tab', { name: 'Workflows', exact: true }).click()
  await expect(page.getByRole('tab', { name: 'Workflows' })).toHaveAttribute(
    'aria-selected',
    'true',
  )
  await page.goBack()
  await expect(page.getByRole('tab', { name: 'Tasks' })).toHaveAttribute('aria-selected', 'true')
  await page.goBack()
  await expect(page.getByRole('heading', { name: 'Reports & AI Dashboards' })).toBeVisible()
  nav = await showNavigation(page)
  await openGroup(nav, 'Reports & AI Dashboards')
  await nav.getByRole('link', { name: 'AI Dashboards', exact: true }).click()
  await expect(page.getByText('No matching links')).toBeVisible()
  await expect(page.getByRole('tab', { name: 'AI Dashboards', exact: true })).toHaveAttribute(
    'aria-selected',
    'true',
  )
})

test('adds edits views and deletes a link', async ({ page }) => {
  await page.goto('/reports')
  await page.getByRole('button', { name: 'Add link', exact: true }).click()
  await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Team insights report')
  await page
    .getByRole('textbox', { name: 'Description', exact: true })
    .fill('Weekly planning and business review.')
  await page.getByRole('textbox', { name: 'Destination URL', exact: true }).fill(link.url)
  await page.getByRole('button', { name: 'Save link' }).click()
  await expect(page.getByRole('heading', { name: 'Team insights report' })).toBeVisible()
  await expect(page.frameLocator('iframe').getByText('Sample report content')).toBeVisible()
  const popupPromise = page.waitForEvent('popup')
  await page.getByRole('link', { name: 'Open externally' }).click()
  const popup = await popupPromise
  await expect(popup).toHaveURL(link.url)
  await popup.close()
  await page.getByRole('button', { name: 'Edit link' }).click()
  await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Updated team insights')
  await page.getByRole('button', { name: 'Save link' }).click()
  await expect(page.getByRole('heading', { name: 'Updated team insights' })).toBeVisible()
  await page.getByRole('button', { name: 'Back to directory' }).click()
  await page.getByRole('button', { name: 'Delete Updated team insights' }).click()
  await page.getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(page.getByRole('link', { name: 'Updated team insights' })).toBeVisible()
  await page.getByRole('button', { name: 'Delete Updated team insights' }).click()
  await page.getByRole('button', { name: 'Delete link', exact: true }).click()
  await expect(page.getByText('Your shared directory starts here')).toBeVisible()
})

test('blocked embedding retains guidance and external opening', async ({ page }) => {
  await page.route('https://reports.example.org/**', (route) =>
    route.fulfill({
      headers: { 'X-Frame-Options': 'DENY' },
      contentType: 'text/html',
      body: 'Report requires external viewing.',
    }),
  )
  await page.goto(`/reports/${link.id}`)
  await expect(page.getByText(/If the frame is blank/)).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open externally' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open externally' })).toHaveAttribute(
    'rel',
    'noopener noreferrer',
  )
  expect(await page.locator('iframe').getAttribute('sandbox')).not.toContain('allow-top-navigation')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.getByRole('button', { name: 'Hide preview' }).click()
  await expect(page.locator('iframe')).toHaveCount(0)
  await expect(page.getByText(/Preview hidden/)).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open externally' })).toBeVisible()
})

test('sharing and short links use a compact callout with optional preview', async ({ page }) => {
  for (const url of [
    'https://tenant.sharepoint.com/shared/dashboard.html',
    'https://aka.ms/shared-report',
    'https://msit.powerbi.com/groups/me/reports/report-id',
  ]) {
    await page.route(`**/api/reports/${link.id}?*`, (route) =>
      route.fulfill({ json: { ...link, url } }),
    )
    await page.goto(`/reports/${link.id}`)
    await expect(page.getByRole('button', { name: 'Try preview' })).toBeVisible()
    await expect(page.locator('iframe')).toHaveCount(0)
    await expect(page.getByRole('link', { name: 'Open externally' })).toHaveAttribute('href', url)
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
    ).toBe(true)
  }
})

test('navigation works by keyboard and mobile drawer restores focus', async ({
  page,
  isMobile,
}) => {
  await page.goto('/reports')
  const toggle = page.getByRole('button', { name: 'Toggle navigation' })
  await toggle.focus()
  await page.keyboard.press('Enter')
  if (isMobile) {
    await expect(page.getByRole('dialog')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).not.toBeVisible()
    await expect(toggle).toBeFocused()
  } else {
    await expect(page.getByRole('navigation', { name: 'Main navigation' })).not.toBeVisible()
    await page.keyboard.press('Enter')
    await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible()
  }
})
