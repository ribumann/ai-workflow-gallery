import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiClient, ApiError } from '../../src/services/apiClient'
import { ReportsPage } from '../../src/pages/Reports/ReportsPage'
import { ReportEditorPage } from '../../src/pages/Reports/ReportEditorPage'
import { ReportViewerPage } from '../../src/pages/Reports/ReportViewerPage'
import type { ReportLink } from '../../src/services/types'
import { canEmbedReportUrl, getReportEmbedCaution, isSafeReportUrl } from '../../src/utils/url'
import { renderWithAuth as render } from './authTestUtils'

vi.mock('../../src/services/apiClient', async (original) => ({
  ...(await original<typeof import('../../src/services/apiClient')>()),
  apiClient: {
    listReports: vi.fn(),
    getReport: vi.fn(),
    createReport: vi.fn(),
    updateReport: vi.fn(),
    deleteReport: vi.fn(),
  },
}))

const item: ReportLink = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Quarterly delivery report',
  description: 'Review delivery outcomes and next steps.',
  url: 'https://reports.example.org/delivery',
  kind: 'Report',
  createdByDisplayName: 'Contributor A',
  lastEditedByDisplayName: 'Contributor B',
  createdAt: '2026-09-10T12:00:00Z',
  lastEditedAt: '2026-09-10T12:00:00Z',
  version: '22222222-2222-2222-2222-222222222222',
}

function TestNavigation() {
  const navigate = useNavigate()
  return <button onClick={() => navigate('/reports/new')}>Start another draft</button>
}

function renderPage(path = '/reports') {
  return render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={[path]}>
        <TestNavigation />
        <Routes>
          <Route path="/reports" element={<ReportsPage />} />
          <Route path="/reports/new" element={<ReportEditorPage />} />
          <Route path="/reports/:id/edit" element={<ReportEditorPage />} />
          <Route path="/reports/:id" element={<ReportViewerPage />} />
        </Routes>
      </MemoryRouter>
    </FluentProvider>,
  )
}

beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(apiClient.listReports).mockResolvedValue({
    items: [item],
    totalCount: 1,
    page: 1,
    pageSize: 25,
  })
  vi.mocked(apiClient.getReport).mockResolvedValue(item)
})

describe('Reports directory', () => {
  it('shows descriptions and both viewing choices', async () => {
    renderPage()
    expect(await screen.findByText(item.description)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'View in app' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Open externally' })).toHaveAttribute('href', item.url)
    expect(screen.getByRole('link', { name: 'Open externally' })).toHaveAttribute(
      'rel',
      'noopener noreferrer',
    )
    await userEvent.click(screen.getByRole('tab', { name: 'All links', exact: true }))
    expect(screen.getByText(item.name)).toBeInTheDocument()
    expect(screen.queryByText('Loading directory...')).not.toBeInTheDocument()
  })

  it('filters by type and searches descriptions', async () => {
    renderPage()
    await screen.findByText(item.name)
    await userEvent.click(screen.getByRole('tab', { name: 'AI Dashboards', exact: true }))
    expect(screen.getByRole('tab', { name: 'AI Dashboards', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    expect(screen.getByRole('tab', { name: 'All links', exact: true })).toHaveAttribute(
      'aria-selected',
      'false',
    )
    await waitFor(() =>
      expect(apiClient.listReports).toHaveBeenLastCalledWith({
        kind: 'AiApp',
        page: 1,
        pageSize: 25,
        q: undefined,
      }),
    )
    await userEvent.type(screen.getByRole('searchbox'), 'outcomes')
    await waitFor(() =>
      expect(apiClient.listReports).toHaveBeenLastCalledWith({
        kind: 'AiApp',
        page: 1,
        pageSize: 25,
        q: 'outcomes',
      }),
    )
  })

  it('distinguishes failed load from empty directory and retries', async () => {
    vi.mocked(apiClient.listReports)
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
    renderPage()
    expect(
      await screen.findByText('Could not load the directory. Please retry.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('Your shared directory starts here')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText('Your shared directory starts here')).toBeInTheDocument()
  })

  it('requires confirmation and retains the dialog on delete failure', async () => {
    vi.mocked(apiClient.deleteReport)
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce()
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: `Delete ${item.name}` }))
    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }))
    expect(apiClient.deleteReport).not.toHaveBeenCalled()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    fireEvent.click(screen.getByRole('button', { name: `Delete ${item.name}` }))
    await userEvent.click(
      await screen.findByRole('button', { name: 'Delete link' }),
    )
    expect(await screen.findByText('Could not delete this link. Please retry.')).toBeInTheDocument()
    vi.mocked(apiClient.listReports).mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 25,
    })
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete link' }),
    )
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText('Your shared directory starts here')).toBeInTheDocument()
  })

  it('paginates all results and normalizes page one without sticking in loading', async () => {
    vi.mocked(apiClient.listReports).mockResolvedValue({
      items: [item],
      totalCount: 26,
      page: 1,
      pageSize: 25,
    })
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Next' }))
    await waitFor(() =>
      expect(apiClient.listReports).toHaveBeenLastCalledWith({
        kind: undefined,
        q: undefined,
        page: 2,
        pageSize: 25,
      }),
    )
    await userEvent.click(await screen.findByRole('button', { name: 'Previous' }))
    await waitFor(() =>
      expect(apiClient.listReports).toHaveBeenLastCalledWith({
        kind: undefined,
        q: undefined,
        page: 1,
        pageSize: 25,
      }),
    )
    await screen.findByText(item.name)
    await userEvent.click(screen.getByRole('tab', { name: 'All links', exact: true }))
    expect(screen.queryByText('Loading directory...')).not.toBeInTheDocument()
    expect(screen.getByText(item.name)).toBeInTheDocument()
  })
})

describe('Reports editor and viewer', () => {
  it('labels existing AiApp links as AI Dashboards without changing the saved kind', async () => {
    const dashboard: ReportLink = { ...item, kind: 'AiApp' }
    vi.mocked(apiClient.listReports).mockResolvedValue({
      items: [dashboard],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    })
    vi.mocked(apiClient.getReport).mockResolvedValue(dashboard)
    vi.mocked(apiClient.updateReport).mockResolvedValue(dashboard)
    renderPage('/reports?kind=AiApp')

    expect(await screen.findByText('AI Dashboard', { exact: true })).toBeInTheDocument()
    expect(
      screen.getByRole('searchbox', { name: 'Search reports and AI dashboards' }),
    ).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'View in app' }))
    expect(await screen.findByTitle(item.name)).toBeInTheDocument()
    expect(screen.getByText('AI Dashboard', { exact: true })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Edit link' }))
    await screen.findByDisplayValue(item.name)
    expect(screen.getByRole('combobox', { name: 'Type' })).toHaveValue('AiApp')
    expect(screen.getByRole('option', { name: 'AI Dashboard' })).toHaveValue('AiApp')
    await userEvent.click(screen.getByRole('button', { name: 'Save link' }))
    await waitFor(() =>
      expect(apiClient.updateReport).toHaveBeenCalledWith(
        item.id,
        expect.objectContaining({ kind: 'AiApp', expectedVersion: item.version }),
      ),
    )
  })

  it('does not let an old save navigate away from a newer draft', async () => {
    let finishSave!: (item: ReportLink) => void
    vi.mocked(apiClient.updateReport).mockReturnValue(
      new Promise((resolve) => {
        finishSave = resolve
      }),
    )
    renderPage(`/reports/${item.id}/edit`)
    await screen.findByDisplayValue(item.name)
    await userEvent.click(screen.getByRole('button', { name: 'Save link' }))
    await userEvent.click(screen.getByRole('button', { name: 'Start another draft' }))
    await userEvent.type(
      screen.getByRole('textbox', { name: 'Name', exact: true }),
      'My newer draft',
    )
    await act(async () => {
      finishSave(item)
    })
    expect(screen.getByDisplayValue('My newer draft')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Add a report or AI dashboard' })).toBeInTheDocument()
  })
  it('creates a link and opens its saved viewer', async () => {
    vi.mocked(apiClient.createReport).mockResolvedValue(item)
    renderPage('/reports/new')
    await userEvent.type(screen.getByRole('textbox', { name: 'Name', exact: true }), item.name)
    await userEvent.type(
      screen.getByRole('textbox', { name: 'Description', exact: true }),
      item.description,
    )
    await userEvent.type(
      screen.getByRole('textbox', { name: 'Destination URL', exact: true }),
      item.url,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Save link' }))
    await waitFor(() =>
      expect(apiClient.createReport).toHaveBeenCalledWith({
        name: item.name,
        description: item.description,
        url: item.url,
        kind: 'Report',
      }),
    )
    expect(await screen.findByTitle(item.name)).toBeInTheDocument()
  })

  it('keeps edited draft on a conflict and reloads only on explicit request', async () => {
    vi.mocked(apiClient.updateReport).mockRejectedValue(new ApiError(409, 'Conflict'))
    renderPage(`/reports/${item.id}/edit`)
    const name = await screen.findByDisplayValue(item.name)
    await userEvent.clear(name)
    await userEvent.type(name, 'My changed name')
    await userEvent.click(screen.getByRole('button', { name: 'Save link' }))
    expect(await screen.findByText(/Someone else changed this link/)).toBeInTheDocument()
    expect(screen.getByDisplayValue('My changed name')).toBeInTheDocument()
    expect(apiClient.updateReport).toHaveBeenCalledWith(
      item.id,
      expect.objectContaining({ expectedVersion: item.version }),
    )
    await userEvent.click(screen.getByRole('button', { name: /Reload latest version/ }))
    expect(await screen.findByDisplayValue(item.name)).toBeInTheDocument()
  })

  it('rejects unsafe destinations before save', async () => {
    renderPage(`/reports/${item.id}/edit`)
    const url = await screen.findByDisplayValue(item.url)
    await userEvent.clear(url)
    await userEvent.type(url, 'javascript:alert(1)')
    await userEvent.click(screen.getByRole('button', { name: 'Save link' }))
    expect(apiClient.updateReport).not.toHaveBeenCalled()
  })

  it('sandboxes external content without top navigation and keeps escape action visible', async () => {
    renderPage(`/reports/${item.id}`)
    const frame = await screen.findByTitle(item.name)
    expect(frame).toHaveAttribute('src', item.url)
    expect(frame).toHaveAttribute('referrerpolicy', 'no-referrer')
    expect(frame.getAttribute('sandbox')).not.toContain('allow-top-navigation')
    expect(screen.getByRole('link', { name: 'Open externally' })).toHaveAttribute(
      'target',
      '_blank',
    )
    expect(screen.getByText(/If the frame is blank/)).toBeInTheDocument()
    expect(
      screen.getByText(/Gallery sign-in does not grant access to the destination/),
    ).toBeInTheDocument()
  })

  it('does not embed gallery origins or render unsafe destination links', async () => {
    vi.mocked(apiClient.getReport).mockResolvedValue({ ...item, url: 'javascript:alert(1)' })
    renderPage(`/reports/${item.id}`)
    expect(await screen.findByText(/not a valid HTTPS URL/)).toBeInTheDocument()
    expect(screen.queryByTitle(item.name)).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Open externally' })).not.toBeInTheDocument()
    expect(
      canEmbedReportUrl('https://gallery.example.org/reports', 'https://gallery.example.org'),
    ).toBe(false)
  })

  it('keeps sharing links compact and lets the user try or hide a preview', async () => {
    vi.mocked(apiClient.getReport).mockResolvedValue({
      ...item,
      url: 'https://tenant.sharepoint.com/shared/dashboard.html',
    })
    renderPage(`/reports/${item.id}`)
    expect(await screen.findByText(/SharePoint pages often block/)).toBeInTheDocument()
    expect(screen.queryByTitle(item.name)).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Try preview' }))
    expect(screen.getByTitle(item.name)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Hide preview' }))
    expect(screen.queryByTitle(item.name)).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Open externally' })).toBeInTheDocument()
  })

  it('leaves opaque frame load results alone and lets the user collapse the preview', async () => {
    renderPage(`/reports/${item.id}`)
    const frame = await screen.findByTitle(item.name)
    fireEvent.load(frame)
    expect(frame).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Hide preview' }))
    expect(screen.queryByTitle(item.name)).not.toBeInTheDocument()
    expect(screen.getByText(/Preview hidden/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Try preview' }))
    expect(screen.getByTitle(item.name)).toBeInTheDocument()
  })

  it('shows a clear not-found state', async () => {
    vi.mocked(apiClient.getReport).mockRejectedValue(new ApiError(404, 'Not found'))
    renderPage(`/reports/${item.id}`)
    expect(await screen.findByText(/This link no longer exists/)).toBeInTheDocument()
    expect(screen.queryByTitle(item.name)).not.toBeInTheDocument()
  })
})

describe('Report URL guard', () => {
  it.each([
    'https://tenant.sharepoint.com/shared/dashboard.html',
    'https://tenant.sharepoint.com./shared/dashboard.html',
    'https://app.powerbi.com/groups/me/reports/report-id',
    'https://msit.powerbi.com/groups/me/apps/app-id/reports/report-id',
    'https://aka.ms/shared-report',
    'https://1drv.ms/shared-file',
  ])('starts likely problematic destinations compact: %s', (url) => {
    expect(getReportEmbedCaution(new URL(url))).toBeDefined()
  })

  it.each([
    'https://app.powerbi.com/reportEmbed?reportId=report-id',
    'https://msit.powerbi.com/reportEmbed?reportId=report-id',
    'https://example.com',
    'https://notsharepoint.com/dashboard',
    'https://sharepoint.com.example.org/dashboard',
    'https://aka.ms.example.org/dashboard',
  ])('does not classify embed URLs or unrelated hosts as blocked: %s', (url) => {
    expect(getReportEmbedCaution(new URL(url))).toBeUndefined()
  })

  it.each([
    'javascript:alert(1)',
    'data:text/html,hi',
    'file:///test',
    'http://example.org',
    '//example.org',
    'https://user:password@example.org',
    'https://example.org\\@evil.example',
    'https://example.org/\npath',
    'https://example.org/with space',
    'https://example.org/\u0085',
    'https://example.org/\u00a0',
    'https://example.org/\u2003',
    'https:///example.org',
    'not a URL',
  ])('rejects %s', (url) => {
    expect(isSafeReportUrl(url)).toBe(false)
  })
  it('accepts HTTPS report URLs including query/fragment', () => {
    expect(isSafeReportUrl('https://reports.example.org/embed?id=123#page2')).toBe(true)
  })
})
