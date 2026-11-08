import { act, fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { SearchPage } from '../../src/pages/Search/SearchPage'
import { apiClient } from '../../src/services/apiClient'
import { renderWithAuth as render } from './authTestUtils'
import type { AuthState } from '../../src/auth/authContext'
import type {
  MethodSummary,
  TaskSummary,
  PagedResult,
  WorkflowSummary,
} from '../../src/services/types'

// Smoke test (T053, issue #53): the Search page is the app's landing page and the entry point for
// quickstart.md Scenario 1 (search -> open a workflow), so it's the highest-value page to cover
// first. Mocks the API client so the test doesn't depend on a live backend.
vi.mock('../../src/services/apiClient', () => ({
  apiClient: {
    listTasks: vi.fn(),
    listWorkflows: vi.fn(),
    listMethods: vi.fn(),
    composeWorkflow: vi.fn(),
  },
}))

const deployment = vi.hoisted(() => ({ publicDemo: false }))
vi.mock('../../src/services/deployment', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../src/services/deployment')>()),
  get publicDemo() {
    return deployment.publicDemo
  },
}))

function HistoryControls() {
  const navigate = useNavigate()
  const location = useLocation()
  return (
    <>
      <output aria-label="Current path">{location.pathname}</output>
      <output aria-label="Current URL">{location.search}</output>
      <button onClick={() => navigate(-1)}>Back</button>
      <button onClick={() => navigate('/?collectionId=curated-tools')}>Change collection</button>
    </>
  )
}

function renderSearchPage(initialEntry = '/', authOverrides: Partial<AuthState> = {}) {
  const url = new URL(initialEntry, window.location.origin)
  window.history.replaceState({}, '', `${url.pathname}${url.search}${url.hash}`)
  window.sessionStorage.clear()
  return render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <SearchPage />
        <HistoryControls />
      </MemoryRouter>
    </FluentProvider>,
    { authState: { publicDemo: deployment.publicDemo, ...authOverrides } },
  )
}

const emptyTasks: PagedResult<TaskSummary> = {
  items: [],
  totalCount: 0,
  page: 1,
  pageSize: 50,
}

const emptyWorkflows: PagedResult<WorkflowSummary> = {
  items: [],
  totalCount: 0,
  page: 1,
  pageSize: 50,
}

describe('SearchPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    deployment.publicDemo = false
  })

  it('renders the page heading and search controls', async () => {
    vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
    vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)

    renderSearchPage()

    expect(screen.getByText('Discover tasks, workflows & methods')).toBeInTheDocument()
    expect(
      screen.queryByText(/Search the catalog of agents, skills, plugins/),
    ).not.toBeInTheDocument()
    expect(screen.getByPlaceholderText('Search by keyword...')).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Search targets' })).toBeInTheDocument()
    await waitFor(() => expect(apiClient.listTasks).toHaveBeenCalled())
  })

  it.each([
    ['tasks', 'New task', '/tasks/new'],
    ['workflows', 'New workflow', '/workflows/new'],
    ['methods', 'New method', '/methods/new'],
  ] as const)('shows one create action for the %s tab', async (tab, label, path) => {
    vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
    vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)
    vi.mocked(apiClient.listMethods).mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 25,
    })

    renderSearchPage(`/?tab=${tab}`)

    const createButtons = screen.getAllByRole('button', { name: /^New (task|workflow|method)$/ })
    expect(createButtons).toHaveLength(1)
    expect(createButtons[0]).toHaveAccessibleName(label)
    fireEvent.click(createButtons[0])
    expect(screen.getByLabelText('Current path')).toHaveTextContent(path)
  })

  it('keeps the proposal composer discoverable from every catalog tab', async () => {
    const user = userEvent.setup()
    vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
    vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)

    renderSearchPage('/', { composerEnabled: true, composerState: 'ready' })

    const composerButton = screen.getByRole('button', { name: 'Workflow composer', exact: true })
    expect(composerButton).toBeVisible()
    await user.click(composerButton)
    expect(await screen.findByRole('textbox', { name: 'What are you trying to achieve?' })).toBeVisible()
    await user.click(await screen.findByRole('button', { name: 'Close' }))

    await user.click(screen.getByRole('tab', { name: 'Workflows', exact: true }))
    expect(screen.getByRole('button', { name: 'Workflow composer', exact: true })).toBeVisible()
    await user.click(screen.getByRole('tab', { name: 'Methods', exact: true }))
    expect(screen.getByRole('button', { name: 'Workflow composer', exact: true })).toBeVisible()
  })

  it.each(['public-techniques', 'curated-tools', 'playground'])(
    'keeps the composer entry point visible in public collection %s when unavailable',
    async (collectionId) => {
      deployment.publicDemo = true
      vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
      vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)

      renderSearchPage(`/?collectionId=${collectionId}`, {
        composerEnabled: false,
        composerState: 'disabled',
      })

      expect(
        await screen.findByRole('button', { name: 'Workflow composer unavailable', exact: true }),
      ).toBeVisible()
      expect(
        screen.getByRole('button', { name: 'Workflow composer unavailable', exact: true }),
      ).toBeDisabled()
    },
  )

  it('keeps public composer proposals from creating catalog items even in a writable collection', async () => {
    const user = userEvent.setup()
    deployment.publicDemo = true
    vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
    vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)

    renderSearchPage('/?collectionId=playground', {
      publicDemo: true,
      composerEnabled: true,
      composerState: 'ready',
      collectionCapabilities: {
        id: 'playground',
        label: 'Playground',
        description: 'Shared, public, disposable content.',
        canWrite: true,
        isDisposable: true,
      },
    })

    await user.click(await screen.findByRole('button', { name: 'Workflow composer', exact: true }))
    await user.type(screen.getByRole('textbox', { name: 'What are you trying to achieve?' }), 'Review evidence')
    vi.mocked(apiClient.composeWorkflow).mockResolvedValue({
      suggestedName: 'Evidence workflow',
      problemStatement: 'Review evidence',
      suggestedOutcome: 'A reviewed summary',
      steps: [{ sequenceOrder: 1, taskId: 'task-1', taskName: 'Review', taskType: 'prompt', targetId: 'microsoft.copilot.chat', notes: null }],
      gaps: [{ description: 'Approval', sequenceHint: 2, suggestedTaskName: 'Approval task', suggestedTaskDescription: 'Approve the summary.' }],
    })
    await user.click(screen.getByRole('button', { name: 'Get a proposal' }))
    expect(await screen.findByText('Evidence workflow')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Review and save workflow' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add task' })).not.toBeInTheDocument()
  })

  it('shows empty-catalog guidance when there are no tasks and no search criteria', async () => {
    vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
    vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)

    renderSearchPage()

    await waitFor(() => expect(screen.getByText('The catalog is empty')).toBeInTheDocument())
    expect(screen.getByText(/Choose .New task. to contribute the first one/)).toBeInTheDocument()
  })

  it.each(['public-techniques', 'curated-tools'])(
    'does not invite public publishing in empty %s',
    async (collectionId) => {
      deployment.publicDemo = true
      vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
      vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)

      renderSearchPage(`/?collectionId=${collectionId}`)

      expect(await screen.findByText('The catalog is empty')).toBeInTheDocument()
      expect(
        screen.getByText(
          'No published tasks are available in this collection. Choose another collection to browse.',
        ),
      ).toBeInTheDocument()
      expect(screen.queryByText(/contribute the first/)).not.toBeInTheDocument()
      expect(
        screen.queryByRole('button', { name: 'New task', exact: true }),
      ).not.toBeInTheDocument()
    },
  )

  it('renders search results in the tasks grid', async () => {
    const task: TaskSummary = {
      id: 'a1',
      name: 'Sample Agent',
      description: 'A sample agent for testing.',
      taskType: 'agent',
      targetIds: ['github.copilot.vscode'],
      format: null,
      setup: null,
      prerequisites: null,
      tags: [],
      hasReferenceLink: true,
      hasInlineContent: false,
      hasAttachedFile: false,
      createdByDisplayName: 'Test User',
      lastEditedAt: new Date().toISOString(),
    }
    vi.mocked(apiClient.listTasks).mockResolvedValue({
      items: [task],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    })
    vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)

    renderSearchPage()

    await waitFor(() => expect(screen.getByText('Sample Agent')).toBeInTheDocument())
    // Row clicks alone would leave the grid unreachable by keyboard, so the name is a real link.
    expect(screen.getByRole('link', { name: 'Sample Agent' })).toHaveAttribute('href', '/tasks/a1')
    expect(screen.getByText('Agent · GitHub Copilot in VS Code · by Test User')).toBeInTheDocument()
    expect(screen.getAllByRole('columnheader')).toHaveLength(1)
  })

  it('shows the server total and page count instead of truncating the catalog', async () => {
    const task: TaskSummary = {
      id: 'a1',
      name: 'Sample Agent',
      description: 'A sample agent for testing.',
      taskType: 'agent',
      targetIds: ['github.copilot.vscode'],
      format: null,
      setup: null,
      prerequisites: null,
      tags: [],
      hasReferenceLink: true,
      hasInlineContent: false,
      hasAttachedFile: false,
      createdByDisplayName: 'Test User',
      lastEditedAt: new Date().toISOString(),
    }
    vi.mocked(apiClient.listTasks).mockResolvedValue({
      items: [task],
      totalCount: 225,
      page: 1,
      pageSize: 50,
    })
    vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)

    renderSearchPage()

    expect(await screen.findByText('Page 1 of 9')).toBeInTheDocument()
    expect(screen.getByText('1–1 of 225 items')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Next', exact: true })).toBeEnabled()
    expect(apiClient.listTasks).toHaveBeenLastCalledWith({
      q: undefined,
      type: undefined,
      targetId: undefined,
      page: 1,
      pageSize: 25,
    })
  })

  it('lists AI methods on the methods tab and leaves the task filters disabled', async () => {
    const method: MethodSummary = {
      id: 'm1',
      title: 'Prime before you prompt',
      summary: 'Gather context first, then metaprompt.',
      createdByDisplayName: 'Test User',
      lastEditedAt: new Date().toISOString(),
    }
    vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
    vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)
    vi.mocked(apiClient.listMethods).mockResolvedValue({
      items: [method],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    })

    renderSearchPage('/?tab=methods')

    await waitFor(() => expect(screen.getByText('Prime before you prompt')).toBeInTheDocument())
    expect(screen.getByText(/Updated .* · by Test User/)).toBeInTheDocument()
    expect(apiClient.listMethods).toHaveBeenCalledWith({ q: undefined, page: 1, pageSize: 25 })
    expect(apiClient.listTasks).not.toHaveBeenCalled()
    for (const combobox of screen.getAllByRole('combobox')) {
      expect(combobox).toBeDisabled()
    }
  })

  it('falls back to the tasks tab for an unknown tab value', async () => {
    vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
    vi.mocked(apiClient.listWorkflows).mockResolvedValue(emptyWorkflows)

    renderSearchPage('/?tab=bogus')

    await waitFor(() => expect(apiClient.listTasks).toHaveBeenCalled())
    expect(apiClient.listMethods).not.toHaveBeenCalled()
  })

  it('supports next, previous, deep links, and browser back at the last page', async () => {
    vi.mocked(apiClient.listTasks).mockImplementation(async (params) => ({
      ...emptyTasks,
      page: params?.page ?? 1,
      totalCount: 225,
      pageSize: 25,
    }))
    renderSearchPage('/?page=8&collectionId=public-techniques')
    expect(await screen.findByText('Page 8 of 9')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Next', exact: true }))
    expect(await screen.findByText('Page 9 of 9')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next', exact: true })).toBeDisabled()
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('collectionId=public-techniques')
    fireEvent.click(screen.getByRole('button', { name: 'Back', exact: true }))
    expect(await screen.findByText('Page 8 of 9')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Previous' }))
    expect(await screen.findByText('Page 7 of 9')).toBeInTheDocument()
    expect(apiClient.listTasks).toHaveBeenLastCalledWith(expect.objectContaining({ page: 7 }))
  })

  it.each(['0', '-1', '1.5', 'Infinity', '9007199254740991', 'garbage'])(
    'treats unsafe page %s as page one',
    async (value) => {
      vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
      renderSearchPage(`/?page=${value}`)
      expect(await screen.findByText('Page 1 of 1')).toBeInTheDocument()
      expect(apiClient.listTasks).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1 }))
      expect(screen.getByRole('button', { name: 'Next', exact: true })).toBeDisabled()
    },
  )

  it('recovers once to the last available page after results shrink', async () => {
    vi.mocked(apiClient.listTasks).mockImplementation(async (params) => ({
      ...emptyTasks,
      page: params?.page ?? 1,
      totalCount: 37,
      pageSize: 25,
    }))
    renderSearchPage('/?page=9&q=agent&type=agent&targetId=github.copilot.vscode')
    expect(await screen.findByText('Page 2 of 2')).toBeInTheDocument()
    expect(apiClient.listTasks).toHaveBeenCalledTimes(2)
    expect(apiClient.listTasks).toHaveBeenLastCalledWith({
      q: 'agent',
      type: 'agent',
      targetId: 'github.copilot.vscode',
      page: 2,
      pageSize: 25,
    })
  })

  it('recovers to page one when the collection becomes empty', async () => {
    vi.mocked(apiClient.listTasks).mockResolvedValue(emptyTasks)
    renderSearchPage('/?page=9')
    expect(await screen.findByText('Page 1 of 1')).toBeInTheDocument()
    expect(screen.getByText('0–0 of 0 items')).toBeInTheDocument()
    expect(apiClient.listTasks).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Next', exact: true })).toBeDisabled()
  })

  it('resets paging on search, tab, type, target, and collection changes', async () => {
    const user = userEvent.setup()
    vi.mocked(apiClient.listTasks).mockResolvedValue({ ...emptyTasks, totalCount: 225 })
    vi.mocked(apiClient.listWorkflows).mockResolvedValue({ ...emptyWorkflows, totalCount: 37 })
    renderSearchPage('/?page=5&type=agent&targetId=github.copilot.vscode')
    await screen.findByText('Page 5 of 9')
    await user.click(screen.getByRole('button', { name: 'Clear target' }))
    await waitFor(() =>
      expect(apiClient.listTasks).toHaveBeenLastCalledWith(
        expect.objectContaining({
          page: 1,
          targetId: undefined,
        }),
      ),
    )
    await user.click(screen.getByRole('button', { name: 'Next', exact: true }))
    await screen.findByText('Page 2 of 9')
    fireEvent.change(screen.getByPlaceholderText('Search by keyword...'), {
      target: { value: 'sample' },
    })
    await waitFor(() =>
      expect(apiClient.listTasks).toHaveBeenLastCalledWith(
        expect.objectContaining({
          page: 1,
          q: 'sample',
        }),
      ),
    )
    await user.click(screen.getByRole('button', { name: 'Next', exact: true }))
    await screen.findByText('Page 2 of 9')
    await user.click(screen.getByRole('combobox', { name: 'Task type' }))
    await user.click(await screen.findByRole('option', { name: 'Prompt', exact: true }))
    await waitFor(() =>
      expect(apiClient.listTasks).toHaveBeenLastCalledWith(
        expect.objectContaining({
          page: 1,
          type: 'prompt',
        }),
      ),
    )
    await user.click(screen.getByRole('button', { name: 'Next', exact: true }))
    await screen.findByText('Page 2 of 9')
    await user.click(screen.getByRole('tab', { name: 'Workflows', exact: true }))
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
    expect(apiClient.listWorkflows).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 1, taskType: 'prompt' }),
    )
    await user.click(screen.getByRole('button', { name: 'Next', exact: true }))
    await screen.findByText('Page 2 of 2')
    await user.click(screen.getByRole('button', { name: 'Change collection' }))
    expect(await screen.findByText('Page 1 of 9')).toBeInTheDocument()
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('?collectionId=curated-tools')
  })

  it('never applies a late response from the previous page', async () => {
    let resolveOld!: (result: PagedResult<TaskSummary>) => void
    vi.mocked(apiClient.listTasks)
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            resolveOld = resolve
          }),
      )
      .mockResolvedValue(emptyTasks)
    renderSearchPage('/?page=9')
    await waitFor(() => expect(apiClient.listTasks).toHaveBeenCalledTimes(1))
    fireEvent.change(screen.getByPlaceholderText('Search by keyword...'), {
      target: { value: 'new' },
    })
    expect(await screen.findByText('Page 1 of 1')).toBeInTheDocument()
    await act(async () => resolveOld({ ...emptyTasks, totalCount: 225, page: 9 }))
    expect(screen.getByText('Page 1 of 1')).toBeInTheDocument()
    expect(screen.queryByText('Page 9 of 9')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('q=new')
  })

  it('reports request failures without presenting an empty successful page', async () => {
    vi.mocked(apiClient.listTasks).mockRejectedValue(new Error('offline'))
    renderSearchPage('/?page=2')
    expect(await screen.findByText(/Could not load the catalog/)).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Catalog pagination' })).not.toBeInTheDocument()
    expect(screen.queryByText('The catalog is empty')).not.toBeInTheDocument()
  })
})
