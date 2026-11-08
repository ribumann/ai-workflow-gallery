import { test, expect } from '@playwright/test'
import { readFile } from 'node:fs/promises'

// Mocked session/API coverage, not live Entra authentication or deployed acceptance.
const taskId = '33333333-3333-3333-3333-333333333333'
const bytes = Buffer.from([0, 255, 128, 13, 10, 123, 0])
const task = {
  id: taskId,
  name: 'Download fixture',
  description: 'Synthetic attachment download coverage.',
  taskType: 'prompt',
  targetIds: ['microsoft.copilot.m365'], format: null, setup: null, prerequisites: null,
  tags: [],
  referenceLink: null,
  inlineContent: null,
  dataSource: null,
  dataOutput: null,
  attachedFile: {
    fileName: 'attachment.bin',
    sizeBytes: bytes.length,
    downloadUrl: 'https://untrusted.example/should-not-be-requested',
  },
  createdBy: { id: 'author', displayName: 'Test author' },
  lastEditedBy: { id: 'author', displayName: 'Test author' },
  createdAt: '2026-01-01T00:00:00Z',
  lastEditedAt: '2026-01-01T00:00:00Z',
  referencedByWorkflowIds: [],
}

test.beforeEach(async ({ page }) => {
  await page.route('**/api/session?*', route => route.fulfill({
    json: { isAuthenticated: true, isAuthorized: true, displayName: 'Test user' },
  }))
  await page.route(`**/api/tasks/${taskId}?*`, route => route.fulfill({ json: task }))
  await page.route(`**/api/tasks/${taskId}/rating?*`, route => route.fulfill({
    json: { thumbsUp: 0, thumbsDown: 0, myRating: null },
  }))
  await page.route(`**/api/tasks/${taskId}/comments?*`, route => route.fulfill({
    json: { items: [] },
  }))
})

test('downloads exact binary bytes and filename without leaving the detail route', async ({ page }) => {
  const untrustedRequests: string[] = []
  await page.route('https://untrusted.example/**', route => {
    untrustedRequests.push(route.request().url())
    return route.abort()
  })
  await page.route(`**/api/tasks/${taskId}/file?*`, route => route.fulfill({
    contentType: 'application/octet-stream', body: bytes,
  }))
  await page.goto(`/tasks/${taskId}`)
  const downloadEvent = page.waitForEvent('download')
  await page.getByRole('button', { name: 'attachment.bin (7 B)' }).click()
  const download = await downloadEvent
  expect(download.suggestedFilename()).toBe('attachment.bin')
  const path = await download.path()
  expect(path).not.toBeNull()
  expect(await readFile(path!)).toEqual(bytes)
  await expect(page).toHaveURL(`/tasks/${taskId}`)
  await expect(page.getByRole('heading', { name: task.name })).toBeVisible()
  expect(untrustedRequests).toEqual([])
})

test('401 keeps detail open with sign-in recovery and no automatic download replay', async ({ page }) => {
  let requests = 0
  await page.route(`**/api/tasks/${taskId}/file?*`, route => {
    requests++
    return route.fulfill({ status: 401, json: { error: 'authentication_required' } })
  })
  await page.goto(`/tasks/${taskId}`)
  const button = page.getByRole('button', { name: 'attachment.bin (7 B)' })
  await button.click()
  await expect(page.getByText('Sign in again, then retry downloading this file.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Sign in again', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: task.name })).toBeVisible()
  await expect(page).toHaveURL(`/tasks/${taskId}`)
  await expect(button).toBeEnabled()
  expect(requests).toBe(1)
  await button.click()
  await expect.poll(() => requests).toBe(2)
})
