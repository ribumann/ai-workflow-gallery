import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiClient, ApiError } from '../../src/services/apiClient'
import { MethodBrowser } from '../../src/components/MethodBrowser'
import { SearchPage } from '../../src/pages/Search/SearchPage'
import { MethodEditorPage } from '../../src/pages/Methods/MethodEditorPage'
import { MethodDetailPage } from '../../src/pages/Methods/MethodDetailPage'
import { validateMethod } from '../../src/pages/Methods/methodUi'
import type { MethodDetail } from '../../src/services/types'
import { method, methodSummary } from './methodFixtures'
import { renderWithAuth as render } from './authTestUtils'

vi.mock('../../src/services/apiClient', async (original) => ({
  ...(await original<typeof import('../../src/services/apiClient')>()),
  apiClient: {
    listMethods: vi.fn(),
    getMethod: vi.fn(),
    createMethod: vi.fn(),
    updateMethod: vi.fn(),
    deleteMethod: vi.fn(),
    getRating: vi.fn(),
    rate: vi.fn(),
    listComments: vi.fn(),
    addComment: vi.fn(),
  },
}))

function NewDraft() {
  const navigate = useNavigate()
  return <button onClick={() => navigate('/methods/new')}>Start another draft</button>
}

function renderPage(path = '/methods') {
  return render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={[path]}>
        <NewDraft />
        <Routes>
          <Route path="/" element={<SearchPage />} />
          {/* MethodBrowser is the picker's search surface; mounting it on a route keeps its
              paging, retry and empty-state behaviour under test without the picker's dialog. */}
          <Route path="/methods" element={<MethodBrowser onSelect={() => {}} />} />
          <Route path="/methods/new" element={<MethodEditorPage />} />
          <Route path="/methods/:id/edit" element={<MethodEditorPage />} />
          <Route path="/methods/:id" element={<MethodDetailPage />} />
        </Routes>
      </MemoryRouter>
    </FluentProvider>,
  )
}

beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(apiClient.listMethods).mockResolvedValue({
    items: [methodSummary],
    totalCount: 1,
    page: 1,
    pageSize: 25,
  })
  vi.mocked(apiClient.getMethod).mockResolvedValue(method)
  vi.mocked(apiClient.createMethod).mockResolvedValue(method)
  vi.mocked(apiClient.updateMethod).mockResolvedValue(method)
  vi.mocked(apiClient.deleteMethod).mockResolvedValue()
  vi.mocked(apiClient.getRating).mockResolvedValue({
    helpfulCount: 0,
    notHelpfulCount: 0,
    myVote: null,
  })
  vi.mocked(apiClient.rate).mockResolvedValue({
    helpfulCount: 1,
    notHelpfulCount: 0,
    myVote: 1,
  })
  vi.mocked(apiClient.listComments).mockResolvedValue({ items: [] })
})

describe('Methods directory', () => {
  it('opens a method by title and renders content and automatic attribution', async () => {
    renderPage('/?tab=methods')
    expect(await screen.findByText(method.summary)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('link', { name: method.title }))
    expect(await screen.findByRole('heading', { name: method.title })).toBeInTheDocument()
    expect(screen.getByText(/Created by Original author/)).toBeInTheDocument()
    expect(screen.getByText(/Last edited by Latest editor/)).toBeInTheDocument()
    expect(screen.getByText(`Version: ${method.version}`)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Start with evidence' })).toBeInTheDocument()
    expect(screen.getByText('source -> claim')).toBeInTheDocument()
  })

  it('offers rating and commenting on a method against the methods engagement routes', async () => {
    renderPage(`/methods/${method.id}`)
    expect(await screen.findByRole('heading', { name: method.title })).toBeInTheDocument()

    await waitFor(() =>
      expect(apiClient.getRating).toHaveBeenCalledWith('methods', method.id, expect.any(String)),
    )
    expect(apiClient.listComments).toHaveBeenCalledWith('methods', method.id)

    vi.mocked(apiClient.addComment).mockResolvedValue({
      id: 'c1',
      body: 'We use this weekly.',
      createdByDisplayName: 'Test User',
      createdAt: new Date().toISOString(),
    })
    await userEvent.type(
      screen.getByPlaceholderText('Share your experience with this...'),
      'We use this weekly.',
    )
    await userEvent.click(screen.getByRole('button', { name: 'Post comment' }))
    await waitFor(() =>
      expect(apiClient.addComment).toHaveBeenCalledWith(
        'methods',
        method.id,
        'We use this weekly.',
      ),
    )
  })

  it('distinguishes failures from empty results and retries', async () => {
    vi.mocked(apiClient.listMethods)
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
    renderPage()
    expect(await screen.findByText(/Could not load methods/)).toBeInTheDocument()
    expect(screen.queryByText('No methods yet')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Retry methods' }))
    expect(await screen.findByText('No methods yet')).toBeInTheDocument()
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'no match' } })
    expect(await screen.findByText('No matching methods')).toBeInTheDocument()
    expect(apiClient.listMethods).toHaveBeenLastCalledWith({ q: 'no match', page: 1, pageSize: 25 })
  })

  it('pages beyond the first 25 and resets paging when searching', async () => {
    vi.mocked(apiClient.listMethods).mockResolvedValue({
      items: [methodSummary],
      totalCount: 26,
      page: 1,
      pageSize: 25,
    })
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Next' }))
    expect(await screen.findByText('Page 2 of 2')).toBeInTheDocument()
    expect(apiClient.listMethods).toHaveBeenLastCalledWith({ q: undefined, page: 2, pageSize: 25 })
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'summary' } })
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
    expect(apiClient.listMethods).toHaveBeenLastCalledWith({ q: 'summary', page: 1, pageSize: 25 })
  })

  it('does not strand results in loading when clearing an already empty search', async () => {
    renderPage()
    await screen.findByText(method.title)
    await userEvent.click(screen.getByRole('searchbox'))
    await userEvent.click(screen.getByRole('button', { name: 'clear', exact: true }))
    expect(screen.getByText(method.title)).toBeInTheDocument()
    expect(screen.queryByText('Loading methods...')).not.toBeInTheDocument()
    expect(apiClient.listMethods).toHaveBeenCalledTimes(1)
  })
})

describe('Method writing', () => {
  it('requires exactly three fields and enforces trimmed content limits', async () => {
    renderPage('/methods/new')
    await userEvent.click(screen.getByRole('button', { name: 'Save method' }))
    expect(screen.getByText('Title is required.')).toBeInTheDocument()
    expect(screen.getByText('Short summary is required.')).toBeInTheDocument()
    expect(screen.getByText('Written approach is required.')).toBeInTheDocument()
    expect(apiClient.createMethod).not.toHaveBeenCalled()
    expect(screen.getAllByRole('textbox')).toHaveLength(3)
    expect(
      validateMethod({
        title: ' '.repeat(10),
        summary: 'x'.repeat(2001),
        approach: 'x'.repeat(100001),
      }),
    ).toEqual({
      title: 'Title is required.',
      summary: `Short summary must be ${(2000).toLocaleString()} characters or fewer.`,
      approach: `Written approach must be ${(100000).toLocaleString()} characters or fewer.`,
    })
    expect(validateMethod({ title: 'x'.repeat(201), summary: 'x', approach: 'x' }).title).toMatch(
      /200/,
    )
    expect(validateMethod({ title: ' x ', summary: ' y ', approach: ' z ' })).toEqual({})
  })

  it('creates using three trimmed fields and previews sanitized Markdown', async () => {
    renderPage('/methods/new')
    fireEvent.change(screen.getByRole('textbox', { name: 'Title' }), {
      target: { value: ` ${method.title} ` },
    })
    fireEvent.change(screen.getByRole('textbox', { name: 'Short summary' }), {
      target: { value: method.summary },
    })
    fireEvent.change(screen.getByRole('textbox', { name: 'Written approach' }), {
      target: { value: method.approach },
    })
    await userEvent.click(screen.getByRole('tab', { name: 'Preview' }))
    expect(screen.getByRole('heading', { name: 'Start with evidence' })).toBeInTheDocument()
    expect(apiClient.createMethod).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Save method' }))
    expect(await screen.findByRole('heading', { name: method.title })).toBeInTheDocument()
    expect(apiClient.createMethod).toHaveBeenCalledWith({
      title: method.title,
      summary: method.summary,
      approach: method.approach,
    })
  })

  it('opens a copy as an unsaved new draft with an incremented version name', async () => {
    renderPage(`/methods/new?copyFrom=${method.id}`)
    expect(
      await screen.findByRole('heading', { name: 'Create new version as copy' }),
    ).toBeInTheDocument()
    expect(screen.getByDisplayValue(`${method.title} (v2)`)).toBeInTheDocument()
    expect(screen.getByDisplayValue(method.summary)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Save method' }))
    await waitFor(() =>
      expect(apiClient.createMethod).toHaveBeenCalledWith({
        title: `${method.title} (v2)`,
        summary: method.summary,
        approach: method.approach,
      }),
    )
    // The source method must stay untouched — a copy only ever creates.
    expect(apiClient.updateMethod).not.toHaveBeenCalled()
  })

  it('offers the copy action on a method and lands in a copy draft', async () => {
    renderPage(`/methods/${method.id}`)
    await userEvent.click(
      await screen.findByRole('button', { name: 'Create new version as copy' }),
    )
    expect(
      await screen.findByRole('heading', { name: 'Create new version as copy' }),
    ).toBeInTheDocument()
    expect(screen.getByDisplayValue(`${method.title} (v2)`)).toBeInTheDocument()
  })

  it('keeps a stale draft and requires explicit reload before saving a new version', async () => {
    vi.mocked(apiClient.updateMethod).mockRejectedValueOnce(new ApiError(409, 'Conflict'))
    renderPage(`/methods/${method.id}/edit`)
    const title = await screen.findByDisplayValue(method.title)
    fireEvent.change(title, { target: { value: 'My draft' } })
    await userEvent.click(screen.getByRole('button', { name: 'Save method' }))
    expect(await screen.findByText(/Someone else changed this method/)).toBeInTheDocument()
    expect(screen.getByDisplayValue('My draft')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save method' })).toBeDisabled()
    expect(apiClient.updateMethod).toHaveBeenCalledWith(
      method.id,
      expect.objectContaining({ title: 'My draft', expectedVersion: method.version }),
    )
    vi.mocked(apiClient.getMethod).mockResolvedValue({
      ...method,
      title: 'Latest saved version',
      version: 'new-version',
    })
    await userEvent.click(screen.getByRole('button', { name: /Reload latest version/ }))
    expect(await screen.findByDisplayValue('Latest saved version')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Save method' }))
    expect(apiClient.updateMethod).toHaveBeenLastCalledWith(
      method.id,
      expect.objectContaining({ expectedVersion: 'new-version' }),
    )
  })

  it.each([
    new ApiError(400, 'Validation', { message: 'Selected content is invalid.' }),
    new ApiError(404, 'Missing'),
    new Error('offline'),
  ])('retains all draft fields after rejected save %s', async (error) => {
    vi.mocked(apiClient.updateMethod).mockRejectedValueOnce(error)
    renderPage(`/methods/${method.id}/edit`)
    fireEvent.change(await screen.findByDisplayValue(method.title), {
      target: { value: 'Unsaved title' },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Save method' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save method' })).toBeEnabled())
    expect(screen.getByDisplayValue('Unsaved title')).toBeInTheDocument()
    expect(screen.getByDisplayValue(method.summary)).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Written approach' })).toHaveValue(method.approach)
  })

  it('retries load failures without exposing an empty edit form', async () => {
    vi.mocked(apiClient.getMethod).mockRejectedValueOnce(new Error('offline'))
    renderPage(`/methods/${method.id}/edit`)
    expect(await screen.findByText('Could not load this method. Please retry.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save method' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByDisplayValue(method.title)).toBeInTheDocument()
  })

  it('ignores an old save after navigating to a fresh draft', async () => {
    let finish!: (item: MethodDetail) => void
    vi.mocked(apiClient.updateMethod).mockReturnValue(
      new Promise((resolve) => {
        finish = resolve
      }),
    )
    renderPage(`/methods/${method.id}/edit`)
    await screen.findByDisplayValue(method.title)
    await userEvent.click(screen.getByRole('button', { name: 'Save method' }))
    await userEvent.click(screen.getByRole('button', { name: 'Start another draft' }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Title' }), {
      target: { value: 'New draft' },
    })
    await act(async () => finish(method))
    expect(screen.getByDisplayValue('New draft')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Create method' })).toBeInTheDocument()
  })
})

describe('Method reading and retirement', () => {
  it('sanitizes scripts, event handlers, unsafe links and embedded objects', async () => {
    vi.mocked(apiClient.getMethod).mockResolvedValue({
      ...method,
      approach:
        '# Safe heading\n\n[Unsafe](javascript:alert%281%29)\n\n[Safe](https://example.org)\n\n<script>alert(1)</script>\n\n<img src=x onerror="alert(1)">\n\n<iframe src="https://example.org"></iframe>',
    })
    const { container } = renderPage(`/methods/${method.id}`)
    expect(await screen.findByRole('heading', { name: 'Safe heading' })).toBeInTheDocument()
    expect(container.querySelector('script, iframe, [onerror], a[href^="javascript:"]')).toBeNull()
    expect(screen.getByRole('link', { name: 'Safe' })).toHaveAttribute(
      'href',
      'https://example.org',
    )
  })

  it('uses API-derived canDelete without changing session permissions', async () => {
    vi.mocked(apiClient.getMethod).mockResolvedValue({ ...method, canDelete: false })
    renderPage(`/methods/${method.id}`)
    await screen.findByRole('heading', { name: method.title })
    expect(screen.queryByRole('button', { name: 'Delete method' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Edit method' })).toBeInTheDocument()
  })

  it('requires confirmation, cancels safely, and deletes an unused method', async () => {
    renderPage(`/methods/${method.id}`)
    fireEvent.click(await screen.findByRole('button', { name: 'Delete method' }))
    expect(apiClient.deleteMethod).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(apiClient.deleteMethod).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Delete method' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Confirm delete' }))
    expect(await screen.findByText('Discover tasks, workflows & methods', { selector: 'h1' })).toBeInTheDocument()
    expect(apiClient.deleteMethod).toHaveBeenCalledWith(method.id)
  })

  it('keeps referenced methods intact and offers actionable task/workflow links', async () => {
    vi.mocked(apiClient.deleteMethod).mockRejectedValue(
      new ApiError(409, 'References', {
        message: 'This method is referenced.',
        referencedByTasks: [{ id: 'task', name: 'Task A' }],
        referencedByWorkflows: [{ id: 'workflow', name: 'Workflow A' }],
      }),
    )
    renderPage(`/methods/${method.id}`)
    await userEvent.click(await screen.findByRole('button', { name: 'Delete method' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirm delete' }))
    const dialog = within(screen.getByRole('dialog'))
    expect(await dialog.findByText('This method is referenced.')).toBeInTheDocument()
    expect(dialog.getByRole('link', { name: 'Task: Task A' })).toHaveAttribute(
      'href',
      '/tasks/task',
    )
    expect(dialog.getByRole('link', { name: 'Workflow: Workflow A' })).toHaveAttribute(
      'href',
      '/workflows/workflow',
    )
    expect(screen.getByRole('heading', { name: method.title, hidden: true })).toBeInTheDocument()
  })

  it.each([403, 500])(
    'surfaces delete failure %i and allows retry without losing detail',
    async (status) => {
      vi.mocked(apiClient.deleteMethod).mockRejectedValueOnce(new ApiError(status, 'failure'))
      renderPage(`/methods/${method.id}`)
      await userEvent.click(await screen.findByRole('button', { name: 'Delete method' }))
      await userEvent.click(screen.getByRole('button', { name: 'Confirm delete' }))
      expect(
        await screen.findByText(status === 403 ? /do not have permission/ : /Could not delete/),
      ).toBeInTheDocument()
      await userEvent.click(screen.getByRole('button', { name: 'Confirm delete' }))
      expect(await screen.findByText('Discover tasks, workflows & methods', { selector: 'h1' })).toBeInTheDocument()
    },
  )

  it('handles missing detail and concurrently deleted methods with recovery', async () => {
    vi.mocked(apiClient.getMethod).mockRejectedValueOnce(new ApiError(404, 'Missing'))
    renderPage(`/methods/${method.id}`)
    expect(await screen.findByText(/method is no longer available/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Delete method' }))
    vi.mocked(apiClient.deleteMethod).mockRejectedValueOnce(new ApiError(404, 'Missing'))
    await userEvent.click(screen.getByRole('button', { name: 'Confirm delete' }))
    expect(await screen.findByText(/method is no longer available/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Back to search' })).toBeInTheDocument()
  })
})
