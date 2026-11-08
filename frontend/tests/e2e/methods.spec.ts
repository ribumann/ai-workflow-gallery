import { test, expect, type Page } from '@playwright/test'
import { method, methodSummary, task, workflow } from '../unit/methodFixtures'
import type { MethodDetail, TaskDetail, WorkflowDetail } from '../../src/services/types'

async function setup(page: Page) {
  let savedMethod: MethodDetail | null = { ...method }
  let savedTask: TaskDetail = { ...task }
  let savedWorkflow: WorkflowDetail = { ...workflow }
  let taskPayload: Record<string, unknown> | undefined
  let workflowPayload: Record<string, unknown> | undefined
  const methods = Array.from({ length: 26 }, (_, i) => ({
    ...methodSummary,
    id: `method-${i}`,
    title: `Approach ${String(i + 1).padStart(2, '0')}`,
  }))
  methods[25] = methodSummary
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const [, , kind, id, subresource] = url.pathname.split('/')
    const verb = request.method()
    if (kind === 'session')
      return route.fulfill({
        json: { isAuthenticated: true, isAuthorized: true, displayName: 'Test contributor' },
      })
    if (subresource === 'rating')
      return route.fulfill({ json: { helpfulCount: 0, notHelpfulCount: 0, myVote: null } })
    if (subresource === 'comments') return route.fulfill({ json: { items: [] } })
    if (kind === 'methods') {
      if (verb === 'DELETE') {
        if (savedTask.methodId || savedWorkflow.methodId)
          return route.fulfill({
            status: 409,
            json: {
              message: 'This method is still referenced.',
              referencedByTasks: savedTask.methodId ? [{ id: task.id, name: task.name }] : [],
              referencedByWorkflows: savedWorkflow.methodId
                ? [{ id: workflow.id, name: workflow.name }]
                : [],
            },
          })
        savedMethod = null
        return route.fulfill({ status: 204 })
      }
      if (verb === 'POST' || verb === 'PUT') {
        const body = request.postDataJSON()
        expect(Object.keys(body).sort()).toEqual(
          (verb === 'POST'
            ? ['title', 'summary', 'approach']
            : ['title', 'summary', 'approach', 'expectedVersion']
          ).sort(),
        )
        if (verb === 'PUT') expect(body.expectedVersion).toBe(savedMethod?.version)
        savedMethod = { ...method, ...body, version: '33333333-3333-3333-3333-333333333333' }
        methods[25] = { ...methodSummary, ...savedMethod }
        return route.fulfill({ status: verb === 'POST' ? 201 : 200, json: savedMethod })
      }
      if (id)
        return route.fulfill(
          savedMethod
            ? { json: savedMethod }
            : { status: 404, json: { message: 'Missing method' } },
        )
      const q = (url.searchParams.get('q') ?? '').toLowerCase()
      const currentPage = Number(url.searchParams.get('page') ?? 1)
      const filtered = methods.filter((item) =>
        `${item.title} ${item.summary}`.toLowerCase().includes(q),
      )
      return route.fulfill({
        json: {
          items: savedMethod ? filtered.slice((currentPage - 1) * 25, currentPage * 25) : [],
          totalCount: savedMethod ? filtered.length : 0,
          page: currentPage,
          pageSize: 25,
        },
      })
    }
    if (kind === 'tasks') {
      if (verb === 'PUT' || verb === 'POST') {
        const form = await new Response(request.postData() ?? '', {
          headers: { 'Content-Type': request.headers()['content-type'] },
        }).formData()
        const metadata = JSON.parse(String(form.get('metadata')))
        taskPayload = metadata
        savedTask = {
          ...savedTask,
          ...metadata,
          method: 'methodId' in metadata ? (metadata.methodId ? method : null) : savedTask.method,
        }
        return route.fulfill({ json: savedTask, status: verb === 'POST' ? 201 : 200 })
      }
      if (id) return route.fulfill({ json: savedTask })
      return route.fulfill({
        json: {
          items: [
            {
              ...task,
              createdByDisplayName: 'Author',
              hasReferenceLink: true,
              hasInlineContent: true,
              hasAttachedFile: true,
            },
          ],
          totalCount: 1,
          page: 1,
          pageSize: 25,
        },
      })
    }
    if (kind === 'workflows') {
      if (verb === 'PUT' || verb === 'POST') {
        const body = request.postDataJSON()
        workflowPayload = body
        savedWorkflow = {
          ...savedWorkflow,
          name: body.name,
          ...('methodId' in body
            ? { methodId: body.methodId, method: body.methodId ? method : null }
            : {}),
        }
        return route.fulfill({ json: savedWorkflow, status: verb === 'POST' ? 201 : 200 })
      }
      if (id) return route.fulfill({ json: savedWorkflow })
      return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 25 } })
    }
    return route.fulfill({ status: 404, json: { message: 'Unexpected test route' } })
  })
  return {
    taskPayload: () => taskPayload,
    workflowPayload: () => workflowPayload,
    linkBoth: () => {
      savedTask = { ...savedTask, methodId: method.id, method }
      savedWorkflow = { ...savedWorkflow, methodId: method.id, method }
    },
  }
}

async function navigation(page: Page) {
  await expect(page.getByRole('button', { name: 'Toggle navigation' })).toBeVisible()
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  if (!(await nav.isVisible()))
    await page.getByRole('button', { name: 'Toggle navigation' }).click()
  return nav
}

async function noOverflow(page: Page) {
  expect(
    await page.evaluate(() => {
      const grid = document.querySelector<HTMLElement>('[role="grid"]')
      return (
        document.documentElement.scrollWidth <= window.innerWidth &&
        (!grid || grid.scrollWidth <= grid.clientWidth)
      )
    }),
  ).toBe(true)
}

test('Catalog opens the selected Methods tab on desktop and mobile', async ({ page, isMobile }) => {
  await setup(page)
  await page.goto('/')
  let nav = await navigation(page)
  await nav.getByRole('link', { name: 'Catalog', exact: true }).click()
  await page.getByRole('tab', { name: 'Methods' }).click()
  await expect(
    page.getByRole('heading', { name: 'Discover tasks, workflows & methods' }),
  ).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Methods' })).toHaveAttribute('aria-selected', 'true')
  if (isMobile) await expect(page.getByRole('dialog')).not.toBeVisible()
  await expect(page.getByText('Approach 01', { exact: true })).toBeVisible()
  await page.getByRole('searchbox').fill(method.title)
  await expect(page.getByText(method.title, { exact: true })).toBeVisible()
  await noOverflow(page)
  await page.screenshot({ path: test.info().outputPath('methods-directory.png'), fullPage: true })
  await page.getByText(method.title, { exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Start with evidence' })).toBeVisible()
  await noOverflow(page)
  await page.screenshot({ path: test.info().outputPath('method-detail.png'), fullPage: true })
  nav = await navigation(page)
  await expect(nav.getByRole('link', { name: 'Catalog' })).toHaveAttribute('aria-current', 'page')
  await expect(nav.getByRole('button', { name: 'Reports & AI Dashboards' })).toHaveAttribute(
    'aria-expanded',
    'false',
  )
  await nav.getByRole('button', { name: 'Reports & AI Dashboards' }).click()
  await expect(nav.getByRole('link', { name: 'AI Dashboards', exact: true })).toHaveAttribute(
    'href',
    '/reports?kind=AiApp',
  )
  await expect(nav.getByRole('button', { name: 'Reports & AI Dashboards' })).toBeVisible()
})

test('creates, previews, edits and deletes a descriptive method with three fields', async ({
  page,
}) => {
  await setup(page)
  await page.goto('/methods/new')
  await expect(page.getByRole('textbox')).toHaveCount(3)
  await page
    .getByRole('textbox', { name: 'Title', exact: true })
    .fill('Review sources before synthesis')
  await page
    .getByRole('textbox', { name: 'Short summary' })
    .fill('Understand source boundaries before combining evidence.')
  await page
    .getByRole('textbox', { name: 'Written approach' })
    .fill(
      '# Read carefully\n\n- Cite evidence\n\n[Unsafe](javascript:alert%281%29)\n<script>window.unsafeMethod = true</script>',
    )
  await page.getByRole('tab', { name: 'Preview' }).click()
  await expect(page.getByRole('heading', { name: 'Read carefully' })).toBeVisible()
  await expect(page.locator('a[href^="javascript:"]')).toHaveCount(0)
  await page.getByRole('button', { name: 'Save method' }).click()
  await expect(page.getByRole('heading', { name: 'Review sources before synthesis' })).toBeVisible()
  await expect(page.getByText(/Created by Original author/)).toBeVisible()
  await page.getByRole('button', { name: 'Edit method' }).click()
  await page
    .getByRole('textbox', { name: 'Title', exact: true })
    .fill('Updated source review method')
  await noOverflow(page)
  await page.screenshot({ path: test.info().outputPath('method-editor.png'), fullPage: true })
  await page.getByRole('button', { name: 'Save method' }).click()
  await expect(page.getByRole('heading', { name: 'Updated source review method' })).toBeVisible()
  await page.getByRole('button', { name: 'Delete method' }).click()
  await page.getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Updated source review method' })).toBeVisible()
  await page.getByRole('button', { name: 'Delete method' }).click()
  await page.getByRole('button', { name: 'Confirm delete' }).click()
  await expect(
    page.getByRole('heading', { name: 'Discover tasks, workflows & methods' }),
  ).toBeVisible()
  await page.goto(`/methods/${method.id}`)
  await expect(page.getByText(/method is no longer available/)).toBeVisible()
})

for (const kind of ['task', 'workflow'] as const) {
  test(`${kind} links a later-page method, reopens and clears without changing existing content`, async ({
    page,
  }) => {
    const state = await setup(page)
    const fixture = kind === 'task' ? task : workflow
    const base = `/${kind}s/${fixture.id}`
    await page.goto(`${base}/edit`)
    await page.getByRole('button', { name: 'Choose method' }).click()
    await page.getByRole('button', { name: 'Next', exact: true }).click()
    await page.getByRole('button', { name: `Select ${method.title}` }).click()
    await noOverflow(page)
    await page.screenshot({
      path: test.info().outputPath(`${kind}-method-selection.png`),
      fullPage: true,
    })
    await page.getByRole('button', { name: 'Save changes' }).click()
    const primary = page.getByRole('region', { name: 'Primary method' })
    await expect(primary.getByRole('link', { name: method.title })).toBeVisible()
    await expect(primary.getByText(method.summary)).toBeVisible()
    await page.reload()
    await expect(primary.getByRole('link', { name: method.title })).toBeVisible()
    await primary.getByRole('link').click()
    await expect(page.getByRole('heading', { name: method.title })).toBeVisible()
    await page.goto(`${base}/edit`)
    await expect(page.getByText(method.title)).toBeVisible()
    await page.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('heading', { name: fixture.name })).toBeVisible()
    expect(kind === 'task' ? state.taskPayload() : state.workflowPayload()).not.toHaveProperty(
      'methodId',
    )
    await page.goto(`${base}/edit`)
    await page.getByRole('button', { name: 'Clear method' }).click()
    await page.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('heading', { name: fixture.name })).toBeVisible()
    await expect(primary).toHaveCount(0)
    expect(kind === 'task' ? state.taskPayload() : state.workflowPayload()).toHaveProperty(
      'methodId',
      null,
    )
    if (kind === 'task') {
      expect(state.taskPayload()).toMatchObject({
        inlineContent: task.inlineContent,
        dataSource: task.dataSource,
        dataOutput: task.dataOutput,
      })
      await expect(page.getByRole('button', { name: /prompt.md/ })).toBeVisible()
    } else {
      expect(state.workflowPayload()?.steps).toEqual(
        workflow.steps.map((step) => ({
          taskId: step.taskId,
          targetId: step.targetId,
          notes: step.notes,
          isManualStep: step.isManualStep,
          manualStepTitle: step.isManualStep ? step.taskName : null,
          manualStepInstructions: step.manualStepInstructions,
          dataSource: step.dataSource,
          dataOutput: step.dataOutput,
        })),
      )
      await expect(page.getByText('Ask a colleague to review.')).toBeVisible()
    }
    await page.goto(`${base}/edit`)
    await expect(page.getByText('No primary method selected.')).toBeVisible()
  })
}

test('referenced deletion identifies both tasks and workflows without removing content', async ({
  page,
}) => {
  const state = await setup(page)
  state.linkBoth()
  await page.goto(`/methods/${method.id}`)
  await page.getByRole('button', { name: 'Delete method' }).click()
  await page.getByRole('button', { name: 'Confirm delete' }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByText('This method is still referenced.')).toBeVisible()
  await expect(dialog.getByRole('link', { name: `Task: ${task.name}` })).toHaveAttribute(
    'href',
    `/tasks/${task.id}`,
  )
  await expect(dialog.getByRole('link', { name: `Workflow: ${workflow.name}` })).toHaveAttribute(
    'href',
    `/workflows/${workflow.id}`,
  )
  await dialog.getByRole('link', { name: `Workflow: ${workflow.name}` }).click()
  await expect(page.getByRole('heading', { name: workflow.name })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Primary method' })).toBeVisible()
})

test('empty search clearing and Enter never strand results or publish a task draft', async ({
  page,
}) => {
  const state = await setup(page)
  await page.goto(`/tasks/${task.id}/edit`)
  await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Not ready to publish')
  await page.getByRole('button', { name: 'Choose method' }).click()
  await expect(page.getByRole('button', { name: 'Select Approach 01', exact: true })).toBeVisible()
  await page.getByRole('searchbox').focus()
  await page.getByRole('button', { name: 'clear', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Select Approach 01', exact: true })).toBeVisible()
  await page.getByRole('searchbox').fill('Evidence-led')
  await page.getByRole('searchbox').press('Enter')
  await expect(page.getByRole('button', { name: `Select ${method.title}` })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue(
    'Not ready to publish',
  )
  expect(state.taskPayload()).toBeUndefined()
  await expect(page).toHaveURL(new RegExp(`/tasks/${task.id}/edit$`))
})
