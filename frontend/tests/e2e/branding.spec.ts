import { test, expect } from '@playwright/test'
import { loadBranding } from '../../scripts/branding.mjs'

const brand = loadBranding()

test('deployment branding preserves desktop/mobile navigation, states and keyboard focus', async ({
  page,
}) => {
  let state: 'empty' | 'error' | 'loading' = 'empty'
  await page.route('**/api/**', async (route) => {
    if (route.request().url().includes('/session')) {
      return route.fulfill({
        json: { isAuthenticated: true, isAuthorized: true, displayName: 'Test User' },
      })
    }
    if (route.request().url().includes('/reports')) {
      if (state === 'loading') await new Promise((resolve) => setTimeout(resolve, 1200))
      if (state === 'error')
        return route.fulfill({ status: 500, json: { error: 'Test unavailable' } })
    }
    return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 25 } })
  })
  const response = await page.goto('/reports')
  expect(await response!.text()).toContain(`<title>${brand.title}</title>`)
  await expect(page).toHaveTitle(brand.title)
  await expect(page.getByRole('link', { name: brand.title, exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Reports & AI Dashboards' })).toBeVisible()
  await expect(page.getByText('Your shared directory starts here')).toBeVisible()
  if (brand.organization)
    await expect(page.getByText(brand.organization, { exact: true })).toBeVisible()
  if (brand.logo) {
    await expect(page.locator(`img[src="${brand.logo}"]`)).toBeVisible()
    expect(
      await page
        .locator(`img[src="${brand.logo}"]`)
        .evaluate((img: HTMLImageElement) => img.naturalWidth),
    ).toBeGreaterThan(0)
  }
  expect(await page.locator('link[rel="icon"]').getAttribute('href')).toBe(
    brand.favicon || '/favicon.svg',
  )
  for (const [label, href] of Object.entries(brand.links || {})) {
    await expect(
      page.locator('footer').getByRole('link', { name: new RegExp(`^${label}$`, 'i') }),
    ).toHaveAttribute('href', href)
  }
  if (!brand.links) await expect(page.locator('footer')).toHaveCount(0)
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.locator('#main-content')).toBeFocused()
  const toggle = page.getByRole('button', { name: 'Toggle navigation' })
  await toggle.focus()
  await page.keyboard.press('Enter')
  if (test.info().project.name.includes('mobile')) {
    await expect(page.getByRole('button', { name: 'Close navigation' })).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(toggle).toBeFocused()
  } else {
    await expect(page.getByRole('navigation', { name: 'Main navigation' })).toHaveCount(0)
    await toggle.click()
  }
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  const accent = await page
    .locator('.fui-FluentProvider')
    .evaluate((node) => getComputedStyle(node).getPropertyValue('--colorBrandBackground').trim())
  expect(accent.toLowerCase()).toBe((brand.accent || '#0f6cbd').toLowerCase())
  await page.screenshot({ path: test.info().outputPath('branding-empty.png'), fullPage: true })
  state = 'loading'
  await page.reload()
  await expect(page.getByText('Loading directory...')).toBeVisible()
  await expect(page.getByText('Your shared directory starts here')).toBeVisible()
  state = 'error'
  await page.reload()
  await expect(page.getByRole('group').filter({ hasText: 'Test unavailable' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Retry', exact: true })).toBeVisible()
  await page.screenshot({ path: test.info().outputPath('branding-error.png'), fullPage: true })
  await page.goto('/integrations/cowork')
  if (brand.publisher) {
    await expect(
      page.getByRole('link', { name: 'Cowork plugin package', exact: true }),
    ).toHaveAttribute('href', '/downloads/ai-workflow-gallery-cowork-plugin.zip')
    expect(
      (await page.request.get('/downloads/ai-workflow-gallery-cowork-plugin.zip')).headers()[
        'content-type'
      ],
    ).toContain('zip')
  } else {
    await expect(
      page.getByText(/Browser fallback available.*Plugin package publication is not configured/),
    ).toBeVisible()
    await expect(
      page.getByRole('link', { name: 'Cowork plugin package', exact: true }),
    ).toHaveCount(0)
  }
})
