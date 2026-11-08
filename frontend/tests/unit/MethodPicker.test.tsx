import { useState } from 'react'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiClient, ApiError } from '../../src/services/apiClient'
import { MethodPicker } from '../../src/components/MethodPicker'
import type { MethodRef, MethodSummary, PagedResult } from '../../src/services/types'
import { method, methodSummary } from './methodFixtures'

vi.mock('../../src/services/apiClient', async (original) => ({
  ...(await original<typeof import('../../src/services/apiClient')>()),
  apiClient: { listMethods: vi.fn(), getMethod: vi.fn() },
}))
const onChange = vi.fn()
const another = { ...methodSummary, id: 'later-method', title: 'Method on page two' }
const page = (items: MethodSummary[], totalCount = items.length): PagedResult<MethodSummary> => ({
  items,
  totalCount,
  page: 1,
  pageSize: 25,
})

function Picker({
  initial = null,
  initialId = null,
}: {
  initial?: MethodRef | null
  initialId?: string | null
}) {
  const [value, setValue] = useState(initial)
  const [id, setId] = useState(initial?.id ?? initialId)
  return (
    <MethodPicker
      methodId={id}
      method={value}
      onChange={(next) => {
        onChange(next)
        setValue(next)
        setId(next?.id ?? null)
      }}
    />
  )
}

function renderPicker(initial?: MethodRef | null, initialId?: string) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter>
        <Picker initial={initial} initialId={initialId} />
      </MemoryRouter>
    </FluentProvider>,
  )
}

beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(apiClient.listMethods).mockResolvedValue(page([methodSummary]))
  vi.mocked(apiClient.getMethod).mockResolvedValue(method)
})

describe('Method picker', () => {
  it('does not emit a change on mount or load/search and clears only explicitly', async () => {
    renderPicker(method)
    expect(onChange).not.toHaveBeenCalled()
    expect(apiClient.listMethods).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Change method' }))
    await screen.findByRole('button', { name: `Select ${method.title}` })
    expect(onChange).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Clear method' }))
    expect(onChange).toHaveBeenCalledExactlyOnceWith(null)
  })

  it('selects beyond page one while keeping the selected label independent of results', async () => {
    vi.mocked(apiClient.listMethods)
      .mockResolvedValueOnce(page([methodSummary], 26))
      .mockResolvedValueOnce(page([another], 26))
    renderPicker(method)
    await userEvent.click(screen.getByRole('button', { name: 'Change method' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Next' }))
    await userEvent.click(await screen.findByRole('button', { name: `Select ${another.title}` }))
    expect(onChange).toHaveBeenCalledWith({
      id: another.id,
      title: another.title,
      summary: another.summary,
    })
    expect(screen.getByText(another.title)).toBeInTheDocument()
    vi.mocked(apiClient.listMethods).mockResolvedValue(page([]))
    await userEvent.click(screen.getByRole('button', { name: 'Change method' }))
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'does not match' } })
    expect(await screen.findByText('No matching methods')).toBeInTheDocument()
    expect(screen.getByText(another.title)).toBeInTheDocument()
    expect(onChange).toHaveBeenCalledTimes(1)
  })

  it('ignores stale search responses and failures', async () => {
    let finishOld!: (value: PagedResult<MethodSummary>) => void
    vi.mocked(apiClient.listMethods).mockImplementation(({ q } = {}) =>
      q === 'old'
        ? new Promise((resolve) => {
            finishOld = resolve
          })
        : Promise.resolve(page(q === 'latest' ? [another] : [methodSummary])),
    )
    renderPicker()
    await userEvent.click(screen.getByRole('button', { name: 'Choose method' }))
    await screen.findByRole('button', { name: `Select ${method.title}` })
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'old' } })
    await waitFor(() => expect(finishOld).toBeDefined())
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'latest' } })
    await screen.findByRole('button', { name: `Select ${another.title}` })
    await act(async () => finishOld(page([methodSummary])))
    expect(screen.queryByRole('button', { name: `Select ${method.title}` })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: `Select ${another.title}` })).toBeInTheDocument()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('preserves a selection on search failure and retries', async () => {
    vi.mocked(apiClient.listMethods)
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue(page([]))
    renderPicker(method)
    await userEvent.click(screen.getByRole('button', { name: 'Change method' }))
    expect(await screen.findByText(/Could not load methods/)).toBeInTheDocument()
    expect(screen.getByText(method.title)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Retry methods' }))
    expect(await screen.findByText('No methods yet')).toBeInTheDocument()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('recovers a missing selected label without clearing or requiring a first-page match', async () => {
    vi.mocked(apiClient.getMethod)
      .mockRejectedValueOnce(new ApiError(404, 'Missing'))
      .mockResolvedValue(method)
    renderPicker(null, method.id)
    expect(await screen.findByText(/Your selection is unchanged/)).toBeInTheDocument()
    expect(onChange).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Retry selected method' }))
    expect(await screen.findByText(method.title)).toBeInTheDocument()
    expect(onChange).not.toHaveBeenCalled()
  })
})
