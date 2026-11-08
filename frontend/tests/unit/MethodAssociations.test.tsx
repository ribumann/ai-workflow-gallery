import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiClient, ApiError } from '../../src/services/apiClient'
import { TaskEditorPage } from '../../src/pages/TaskEditor/TaskEditorPage'
import { TaskDetailPage } from '../../src/pages/TaskDetail/TaskDetailPage'
import { WorkflowEditorPage } from '../../src/pages/WorkflowEditor/WorkflowEditorPage'
import { WorkflowDetailPage } from '../../src/pages/WorkflowDetail/WorkflowDetailPage'
import { method, methodSummary, task, workflow } from './methodFixtures'
import type { TaskDetail, WorkflowDetail, WorkflowSaveRequest } from '../../src/services/types'
import { renderWithAuth as render } from './authTestUtils'

vi.mock('../../src/services/apiClient', async (original) => ({
  ...(await original<typeof import('../../src/services/apiClient')>()),
  apiClient: {
    listMethods: vi.fn(),
    getMethod: vi.fn(),
    listTasks: vi.fn(),
    getTask: vi.fn(),
    updateTask: vi.fn(),
    createTask: vi.fn(),
    getWorkflow: vi.fn(),
    updateWorkflow: vi.fn(),
    createWorkflow: vi.fn(),
  },
}))
vi.mock('../../src/components/RatingWidget', () => ({ RatingWidget: () => null }))
vi.mock('../../src/components/CommentsSection', () => ({ CommentsSection: () => null }))

let savedTask: TaskDetail
let savedWorkflow: WorkflowDetail
const ref = { id: method.id, title: method.title, summary: method.summary }

function DraftNavigation() {
  const navigate = useNavigate()
  return (
    <>
      <button onClick={() => navigate('/tasks/new')}>New task draft</button>
      <button onClick={() => navigate('/workflows/new')}>New workflow draft</button>
    </>
  )
}

function renderPage(path: string, state?: unknown) {
  // A location object needs its query string in `search`; keeping it in `pathname` would make
  // `/tasks/new?copyFrom=x` match the `/tasks/:id` detail route instead.
  const [pathname, query] = path.split('?')
  return render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={[{ pathname, search: query ? `?${query}` : '', state }]}>
        <DraftNavigation />
        <Routes>
          <Route path="/tasks/new" element={<TaskEditorPage />} />
          <Route path="/tasks/:id/edit" element={<TaskEditorPage />} />
          <Route path="/tasks/:id" element={<TaskDetailPage />} />
          <Route path="/workflows/new" element={<WorkflowEditorPage />} />
          <Route path="/workflows/:id/edit" element={<WorkflowEditorPage />} />
          <Route path="/workflows/:id" element={<WorkflowDetailPage />} />
          <Route path="/methods/:id" element={<h1>Linked method</h1>} />
        </Routes>
      </MemoryRouter>
    </FluentProvider>,
  )
}

beforeEach(() => {
  vi.resetAllMocks()
  savedTask = { ...task }
  savedWorkflow = { ...workflow }
  vi.mocked(apiClient.getTask).mockImplementation(async () => savedTask)
  vi.mocked(apiClient.getWorkflow).mockImplementation(async () => savedWorkflow)
  vi.mocked(apiClient.listTasks).mockResolvedValue({
    items: [
      {
        ...task,
        hasReferenceLink: true,
        hasInlineContent: true,
        hasAttachedFile: true,
        createdByDisplayName: 'Author',
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 25,
  })
  vi.mocked(apiClient.listMethods).mockResolvedValue({
    items: [methodSummary],
    totalCount: 1,
    page: 1,
    pageSize: 25,
  })
  vi.mocked(apiClient.getMethod).mockResolvedValue(method)
  vi.mocked(apiClient.updateTask).mockImplementation(async (_, input) => {
    savedTask = {
      ...savedTask,
      name: input.name,
      ...(input.methodId !== undefined
        ? { methodId: input.methodId, method: input.methodId ? ref : null }
        : {}),
    }
    return savedTask
  })
  vi.mocked(apiClient.updateWorkflow).mockImplementation(async (_, input) => {
    savedWorkflow = {
      ...savedWorkflow,
      name: input.name,
      ...(input.methodId !== undefined
        ? { methodId: input.methodId, method: input.methodId ? ref : null }
        : {}),
    }
    return savedWorkflow
  })
  vi.mocked(apiClient.createTask).mockResolvedValue(task)
  vi.mocked(apiClient.createWorkflow).mockResolvedValue(workflow)
})

const paths = [
  { kind: 'task', base: `/tasks/${task.id}`, fixture: task, editButton: 'Edit' },
  {
    kind: 'workflow',
    base: `/workflows/${workflow.id}`,
    fixture: workflow,
    editButton: 'Edit workflow',
  },
] as const

describe.each(paths)('$kind primary method', ({ kind, base, fixture, editButton }) => {
  const update = () =>
    kind === 'task' ? vi.mocked(apiClient.updateTask) : vi.mocked(apiClient.updateWorkflow)
  const linkExisting = () => {
    if (kind === 'task') savedTask = { ...savedTask, methodId: method.id, method: ref }
    else savedWorkflow = { ...savedWorkflow, methodId: method.id, method: ref }
  }

  it('links, saves, reopens and clears without changing content or workflow steps', async () => {
    renderPage(`${base}/edit`)
    await screen.findByDisplayValue(fixture.name)
    await userEvent.click(screen.getByRole('button', { name: 'Choose method' }))
    await userEvent.click(await screen.findByRole('button', { name: `Select ${method.title}` }))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    const region = await screen.findByRole('region', { name: 'Primary method' })
    expect(within(region).getByRole('link', { name: method.title })).toHaveAttribute(
      'href',
      `/methods/${method.id}`,
    )
    expect(within(region).getByText(method.summary)).toBeInTheDocument()
    expect(update()).toHaveBeenLastCalledWith(
      fixture.id,
      expect.objectContaining({ methodId: method.id }),
    )
    await userEvent.click(screen.getByRole('button', { name: editButton, exact: true }))
    await screen.findByDisplayValue(fixture.name)
    expect(screen.getByText(method.title)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Clear method' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await screen.findByRole('heading', { name: fixture.name })
    expect(screen.queryByRole('region', { name: 'Primary method' })).not.toBeInTheDocument()
    expect(update()).toHaveBeenLastCalledWith(
      fixture.id,
      expect.objectContaining({ methodId: null }),
    )
    if (kind === 'task') {
      const payload = vi.mocked(apiClient.updateTask).mock.calls[1][1]
      expect(payload).toMatchObject({
        inlineContent: task.inlineContent,
        referenceLink: task.referenceLink,
        dataSource: task.dataSource,
        dataOutput: task.dataOutput,
        tags: task.tags,
        file: null,
      })
      expect(screen.getByRole('button', { name: /prompt.md/ })).toBeInTheDocument()
    } else {
      const payload = vi.mocked(apiClient.updateWorkflow).mock.calls[1][1]
      expect(payload.steps).toEqual(expectedSteps())
      expect(screen.getByText('Ask a colleague to review.')).toBeInTheDocument()
    }
    await userEvent.click(screen.getByRole('button', { name: editButton, exact: true }))
    expect(await screen.findByText('No primary method selected.')).toBeInTheDocument()
  })

  it('omits untouched associations even for a loaded linked entity', async () => {
    linkExisting()
    renderPage(`${base}/edit`)
    fireEvent.change(await screen.findByDisplayValue(fixture.name), {
      target: { value: 'Unrelated change' },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await screen.findByRole('heading', { name: 'Unrelated change' })
    expect(update().mock.calls[0][1]).not.toHaveProperty('methodId')
    expect(screen.getByRole('region', { name: 'Primary method' })).toBeInTheDocument()
  })

  it('supports old/imported details with no association properties', async () => {
    renderPage(`${base}/edit`)
    await screen.findByDisplayValue(fixture.name)
    expect(screen.getByText('No primary method selected.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await screen.findByRole('heading', { name: fixture.name })
    expect(update().mock.calls[0][1]).not.toHaveProperty('methodId')
    expect(screen.queryByRole('region', { name: 'Primary method' })).not.toBeInTheDocument()
  })

  it('preserves a selected method and draft on validation failure then retries', async () => {
    if (kind === 'task')
      vi.mocked(apiClient.updateTask).mockRejectedValueOnce(
        new ApiError(400, 'Invalid', { message: 'Method is no longer available.' }),
      )
    else
      vi.mocked(apiClient.updateWorkflow).mockRejectedValueOnce(
        new ApiError(400, 'Invalid', { message: 'Method is no longer available.' }),
      )
    renderPage(`${base}/edit`)
    fireEvent.change(await screen.findByDisplayValue(fixture.name), {
      target: { value: 'Draft title' },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Choose method' }))
    await userEvent.click(await screen.findByRole('button', { name: `Select ${method.title}` }))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(await screen.findByText('Method is no longer available.')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Draft title')).toBeInTheDocument()
    expect(screen.getByText(method.title)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Clear method' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await screen.findByRole('heading', { name: 'Draft title' })
    expect(update()).toHaveBeenLastCalledWith(
      fixture.id,
      expect.objectContaining({ methodId: null }),
    )
  })

  it('retains explicit clear and other draft fields through concurrency conflict', async () => {
    linkExisting()
    const conflict = {
      ...fixture,
      methodId: method.id,
      method: ref,
      lastEditedAt: '2026-09-11T10:00:00Z',
    }
    if (kind === 'task')
      vi.mocked(apiClient.updateTask).mockRejectedValueOnce(new ApiError(409, 'Conflict', conflict))
    else
      vi.mocked(apiClient.updateWorkflow).mockRejectedValueOnce(
        new ApiError(409, 'Conflict', conflict),
      )
    renderPage(`${base}/edit`)
    fireEvent.change(await screen.findByDisplayValue(fixture.name), {
      target: { value: 'My concurrent draft' },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Clear method' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(await screen.findByText(/changed since you loaded it/)).toBeInTheDocument()
    expect(screen.getByDisplayValue('My concurrent draft')).toBeInTheDocument()
    expect(screen.getByText('No primary method selected.')).toBeInTheDocument()
    await userEvent.click(
      screen.getByRole('button', {
        name: kind === 'task' ? 'Save my changes anyway' : 'Keep my changes',
      }),
    )
    if (kind === 'workflow')
      await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await screen.findByRole('heading', { name: 'My concurrent draft' })
    expect(update()).toHaveBeenLastCalledWith(
      fixture.id,
      expect.objectContaining({ methodId: null }),
    )
    if (kind === 'workflow')
      expect(vi.mocked(apiClient.updateWorkflow).mock.calls[1][1].steps).toEqual(expectedSteps())
  })

  it('reloads conflict association only on request and resets explicit-change tracking', async () => {
    const conflict = {
      ...fixture,
      name: 'Their version',
      methodId: method.id,
      method: ref,
      lastEditedAt: '2026-09-11T10:00:00Z',
    }
    if (kind === 'task')
      vi.mocked(apiClient.updateTask).mockRejectedValueOnce(new ApiError(409, 'Conflict', conflict))
    else
      vi.mocked(apiClient.updateWorkflow).mockRejectedValueOnce(
        new ApiError(409, 'Conflict', conflict),
      )
    renderPage(`${base}/edit`)
    await screen.findByDisplayValue(fixture.name)
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Reload their version' }))
    expect(screen.getByDisplayValue('Their version')).toBeInTheDocument()
    expect(screen.getByText(method.title)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await screen.findByRole('heading', { name: 'Their version' })
    expect(update().mock.calls[1][1]).not.toHaveProperty('methodId')
  })

  it('carries the primary method into a copy, which creates rather than updates', async () => {
    linkExisting()
    const createPayload = () =>
      kind === 'task'
        ? vi.mocked(apiClient.createTask).mock.calls[0]?.[0]
        : vi.mocked(apiClient.createWorkflow).mock.calls[0]?.[0]
    const createButton = kind === 'task' ? 'Create task' : 'Create workflow'

    renderPage(`/${kind}s/new?copyFrom=${fixture.id}`)
    expect(await screen.findByDisplayValue(`${fixture.name} (v2)`)).toBeInTheDocument()
    expect(await screen.findByText(method.title)).toBeInTheDocument()

    await waitFor(() => expect(screen.getByRole('button', { name: createButton })).toBeEnabled())
    await userEvent.click(screen.getByRole('button', { name: createButton }))
    // The picker shows the source's method, so the create payload must carry it — unlike an
    // edit, where an absent methodId means "leave the association alone".
    await waitFor(() =>
      expect(createPayload()).toMatchObject({
        name: `${fixture.name} (v2)`,
        methodId: method.id,
      }),
    )
    expect(update()).not.toHaveBeenCalled()
  })

  it('starts a new unlinked draft after editing a linked item', async () => {
    linkExisting()
    renderPage(`${base}/edit`)
    await screen.findByDisplayValue(fixture.name)
    await userEvent.click(screen.getByRole('button', { name: `New ${kind} draft` }))
    expect(screen.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('')
    expect(screen.getByText('No primary method selected.')).toBeInTheDocument()
  })
})

function expectedSteps(): WorkflowSaveRequest['steps'] {
  return workflow.steps.map((step) => ({
    taskId: step.taskId,
    targetId: step.targetId,
    notes: step.notes,
    isManualStep: step.isManualStep,
    manualStepTitle: step.isManualStep ? step.taskName : null,
    manualStepInstructions: step.manualStepInstructions,
    dataSource: step.dataSource,
    dataOutput: step.dataOutput,
  }))
}

describe('Composer and imported content compatibility', () => {
  it('does not submit a task draft when Enter is used to search for a method', async () => {
    renderPage(`/tasks/${task.id}/edit`)
    fireEvent.change(await screen.findByDisplayValue(task.name), {
      target: { value: 'Not ready to publish' },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Choose method' }))
    await userEvent.type(screen.getByRole('searchbox'), 'evidence{Enter}')
    expect(apiClient.updateTask).not.toHaveBeenCalled()
    expect(screen.getByDisplayValue('Not ready to publish')).toBeInTheDocument()
    await screen.findByRole('button', { name: `Select ${method.title}` })
  })

  it('saves a task composer draft with an uploaded file and no method', async () => {
    const { container } = renderPage('/tasks/new', {
      taskComposerPrefill: {
        suggestedName: 'Composer task',
        suggestedDescription: 'Suggested summary',
      },
    })
    const file = new File(['# Imported prompt\nRetain these instructions.'], 'imported.md', {
      type: 'text/markdown',
    })
    fireEvent.change(container.querySelector('input[type=file]')!, { target: { files: [file] } })
    await userEvent.type(screen.getByRole('combobox', { name: 'Works in' }), 'Microsoft 365')
    await userEvent.click(screen.getByRole('menuitemcheckbox', { name: 'Microsoft 365 Copilot', exact: true }))
    await userEvent.click(screen.getByRole('button', { name: 'Create task' }))
    await waitFor(() => expect(apiClient.createTask).toHaveBeenCalled())
    expect(vi.mocked(apiClient.createTask).mock.calls[0][0]).toMatchObject({
      name: 'Composer task',
      description: 'Suggested summary',
      file,
    })
    expect(vi.mocked(apiClient.createTask).mock.calls[0][0]).not.toHaveProperty('methodId')
  })

  it('saves a composer workflow draft with ordered real tasks and no method', async () => {
    renderPage('/workflows/new', {
      aiComposerPrefill: {
        name: 'Composer workflow',
        problemStatement: 'Problem',
        desiredOutcome: 'Outcome',
        steps: [
          {
            taskId: task.id,
            taskName: task.name,
            taskType: task.taskType,
            targetId: 'microsoft.copilot.m365',
            notes: 'First',
          },
          {
            taskId: task.id,
            taskName: task.name,
            taskType: task.taskType,
            targetId: 'github.copilot.vscode',
            notes: 'Second',
          },
        ],
      },
    })
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Create workflow' })).toBeEnabled(),
    )
    await userEvent.click(screen.getByRole('button', { name: 'Create workflow' }))
    await waitFor(() => expect(apiClient.createWorkflow).toHaveBeenCalled())
    const input = vi.mocked(apiClient.createWorkflow).mock.calls[0][0]
    expect(input).not.toHaveProperty('methodId')
    expect(input.steps.map((step) => step.notes)).toEqual(['First', 'Second'])
    expect(input.steps.map((step) => step.targetId)).toEqual(['microsoft.copilot.m365', 'github.copilot.vscode'])
  })
})
