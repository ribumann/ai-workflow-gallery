import { test, expect, type Page } from '@playwright/test'

// Scenario 1 — Discover (quickstart.md, User Story 1, P1). `/api/session` is mocked as an
// authenticated, in-domain user so RequireAuth's sign-in gate (api/src/Functions/GetSession.cs)
// passes without a real Entra ID sign-in flow. All other `/api/*` calls are mocked via route
// interception so this suite is self-contained.

const mockWorkflow = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Draft and publish a release announcement',
  problemStatement: 'Release notes take too long to write and review.',
  desiredOutcome: 'A polished announcement is drafted, reviewed, and published within an hour.',
  stepCount: 2,
  targetIds: ['github.copilot.vscode', 'microsoft.copilot.m365'],
  createdByDisplayName: 'Anonymous contributor',
  lastEditedAt: '2026-01-01T00:00:00Z',
}

const mockWorkflowDetail = {
  id: mockWorkflow.id,
  name: mockWorkflow.name,
  problemStatement: mockWorkflow.problemStatement,
  desiredOutcome: mockWorkflow.desiredOutcome,
  steps: [
    {
      sequenceOrder: 1,
      taskId: '22222222-2222-2222-2222-222222222222',
      taskAvailable: true,
      taskName: 'Release notes drafting prompt',
      taskType: 'prompt',
      targetId: 'github.copilot.vscode',
      taskTargetIds: ['github.copilot.vscode'],
      notes: null,
      isManualStep: false,
      manualStepInstructions: null,
      dataSource: null,
      dataOutput: null,
    },
    {
      sequenceOrder: 2,
      taskId: null,
      taskAvailable: false,
      taskName: 'Stakeholder review',
      taskType: null,
      targetId: null,
      taskTargetIds: [],
      notes: null,
      isManualStep: true,
      manualStepInstructions: 'Have the PM sign off before publishing.',
      dataSource: null,
      dataOutput: null,
    },
  ],
  createdBy: { id: 'u1', displayName: 'Anonymous contributor' },
  createdAt: '2026-01-01T00:00:00Z',
  lastEditedBy: { id: 'u1', displayName: 'Anonymous contributor' },
  lastEditedAt: '2026-01-01T00:00:00Z',
}

async function mockApis(page: Page, options: { workflows: unknown[] }) {
  await page.route('**/api/session?*', (route) =>
    route.fulfill({
      json: { isAuthenticated: true, isAuthorized: true, displayName: 'Test User' },
    }),
  )
  await page.route('**/api/tasks?*', (route) =>
    route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 50 } }),
  )
  await page.route('**/api/workflows?*', (route) => {
    const url = new URL(route.request().url())
    const q = url.searchParams.get('q') ?? ''
    const matches = q ? options.workflows.filter(() => false) : options.workflows
    // A non-empty search term is treated as "no matches" for this mock unless it matches the name.
    const filtered = q
      ? options.workflows.filter((w) =>
          (w as { name: string }).name.toLowerCase().includes(q.toLowerCase()),
        )
      : matches
    route.fulfill({
      json: { items: filtered, totalCount: filtered.length, page: 1, pageSize: 50 },
    })
  })
  await page.route(`**/api/workflows/${mockWorkflow.id}?*`, (route) =>
    route.fulfill({ json: mockWorkflowDetail }),
  )
  await page.route(`**/api/workflows/${mockWorkflow.id}/rating?*`, (route) =>
    route.fulfill({ json: { helpfulCount: 0, notHelpfulCount: 0, myVote: null } }),
  )
  await page.route(`**/api/workflows/${mockWorkflow.id}/comments?*`, (route) =>
    route.fulfill({ json: { items: [] } }),
  )
}

test.describe('Scenario 1 — Discover', () => {
  test('landing page shows the search screen once signed in', async ({ page }) => {
    await mockApis(page, { workflows: [] })
    await page.goto('/')

    await expect(page.getByRole('heading', { name: 'Discover tasks, workflows & methods' })).toBeVisible()
    await expect(page.getByPlaceholder('Search by keyword...')).toBeVisible()
  })

  test('feedback dialog exposes its form controls to the accessibility tree', async ({ page }) => {
    await mockApis(page, { workflows: [] })
    await page.goto('/')

    await page.getByRole('button', { name: 'Give feedback' }).click()

    const dialog = page.getByRole('dialog', { name: 'Provide feedback' })
    await expect(dialog).toBeVisible()
    await expect(dialog.getByRole('textbox', { name: "What's on your mind?" })).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Submit feedback' })).toBeDisabled()
  })

  test('searching with no matches shows empty-state guidance', async ({ page }) => {
    await mockApis(page, { workflows: [] })
    await page.goto('/')

    await page.getByPlaceholder('Search by keyword...').fill('no-such-task-xyz')
    await expect(page.getByText('No matches found')).toBeVisible()
  })

  test('finding and opening a workflow shows its problem, outcome, and ordered steps', async ({
    page,
  }) => {
    await mockApis(page, { workflows: [mockWorkflow] })
    await page.goto('/')

    await page.getByRole('tab', { name: 'Workflows' }).click()
    await expect(page.getByText(mockWorkflow.name)).toBeVisible()

    await page.getByText(mockWorkflow.name).click()

    await expect(page.getByRole('heading', { name: mockWorkflow.name })).toBeVisible()
    await expect(page.getByText(mockWorkflow.problemStatement)).toBeVisible()
    await expect(page.getByText(mockWorkflow.desiredOutcome)).toBeVisible()
    await expect(page.getByText('Release notes drafting prompt')).toBeVisible()
    await expect(page.getByText('Manual step')).toBeVisible()
    await expect(page.locator('#main-content').getByText('GitHub Copilot in VS Code', { exact: true })).toBeVisible()
    // Manual steps show instructions, not a tool-surface badge.
    await expect(page.getByText('Have the PM sign off before publishing.')).toBeVisible()
  })
})
