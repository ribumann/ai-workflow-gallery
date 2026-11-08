import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { describe, expect, it, vi } from 'vitest'
import { AiComposerPanel } from '../../src/components/AiComposerPanel'
import { apiClient, ApiError } from '../../src/services/apiClient'
import type { ComposeResponse } from '../../src/services/types'

// Covers the AI assistant fly-in (T078-T080, T092, issue #59): goal submission, success/error/
// rate-limited rendering, and the "Create workflow from this" / "Add task" hand-off actions
// (T083/T087). Mocks the API client and react-router's navigate so the test doesn't depend on a
// live backend or a real router.
vi.mock('../../src/services/apiClient', async () => {
  const actual = await vi.importActual<typeof import('../../src/services/apiClient')>(
    '../../src/services/apiClient',
  )
  return {
    ...actual,
    apiClient: {
      composeWorkflow: vi.fn(),
    },
  }
})

const mockNavigate = vi.fn()
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  }
})

function renderPanel(open = true, readOnly = false, maxGoalLength = 1000, canWrite = !readOnly) {
  const onOpenChange = vi.fn()
  render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter>
        <AiComposerPanel open={open} onOpenChange={onOpenChange} readOnly={readOnly}
          canWrite={canWrite} maxGoalLength={maxGoalLength} />
      </MemoryRouter>
    </FluentProvider>,
  )
  return { onOpenChange }
}

const sampleResponse: ComposeResponse = {
  suggestedName: 'Weekly Customer Pulse',
  problemStatement: 'Summarize weekly customer feedback and draft a status update.',
  suggestedOutcome: 'A concise weekly status update.',
  steps: [
    {
      sequenceOrder: 1,
      taskId: 'task-1',
      taskName: 'Feedback Summarizer',
      taskType: 'prompt',
      targetId: 'microsoft.copilot.m365',
      notes: 'Run first',
    },
  ],
  gaps: [
    {
      description: 'No task drafts the final status update.',
      sequenceHint: 2,
      suggestedTaskName: 'Status Update Drafter',
      suggestedTaskDescription: 'Drafts a status update from a summary.',
    },
  ],
}

describe('AiComposerPanel', () => {
  it('renders anonymous proposals without authoring actions or a sign-in claim', async () => {
    vi.mocked(apiClient.composeWorkflow).mockResolvedValue(sampleResponse)
    renderPanel(true, true)
    const user = userEvent.setup()
    await user.type(screen.getByRole('textbox'), 'Summarize evidence')
    await user.click(screen.getByRole('button', { name: /get a proposal/i }))
    expect(await screen.findByText('Weekly Customer Pulse')).toBeVisible()
    expect(screen.getByText(/this read-only collection cannot save it or open it as an editable draft/i)).toBeVisible()
    expect(screen.getByText(/not saved as gallery history/)).toBeVisible()
    expect(screen.queryByRole('button', { name: /review and save workflow|add task/i })).not.toBeInTheDocument()
  })

  it('never exposes proposal-to-item actions in read-only mode even if writes are otherwise allowed', async () => {
    vi.mocked(apiClient.composeWorkflow).mockResolvedValue(sampleResponse)
    renderPanel(true, true, 1000, true)
    const user = userEvent.setup()
    await user.type(screen.getByRole('textbox'), 'Summarize evidence')
    await user.click(screen.getByRole('button', { name: /get a proposal/i }))

    expect(await screen.findByText('Weekly Customer Pulse')).toBeVisible()
    expect(screen.queryByRole('button', { name: /review and save workflow|add task/i })).not.toBeInTheDocument()
  })

  it('uses the server public goal limit', async () => {
    renderPanel(true, true, 12)
    await userEvent.setup().type(screen.getByRole('textbox'), 'a goal longer than twelve')
    expect(screen.getByText('Goal must be 12 characters or fewer.')).toBeVisible()
    expect(screen.getByRole('button', { name: /get a proposal/i })).toBeDisabled()
  })

  it.each([403, 503, 422])('public failure %i does not offer forbidden manual authoring or sign-in', async (status) => {
    vi.mocked(apiClient.composeWorkflow).mockRejectedValue(new ApiError(status, 'Unavailable'))
    renderPanel(true, true)
    const user = userEvent.setup()
    await user.type(screen.getByRole('textbox'), 'Summarize evidence')
    await user.click(screen.getByRole('button', { name: /get a proposal/i }))
    const message = status === 403 ? /Anonymous proposals are not enabled/ :
      status === 503 ? /The Workflow composer is unavailable/ : /No relevant published tasks/
    expect(await screen.findByText(message)).not.toHaveTextContent(/sign in|create a workflow manually/i)
    expect(screen.getByRole('textbox')).toHaveValue('Summarize evidence')
  })
  it.each([404, 503])('explains unavailable Composer without claiming a network failure (%i)', async (status) => {
    vi.mocked(apiClient.composeWorkflow).mockRejectedValue(new ApiError(status, 'Unavailable'))
    renderPanel()
    const user = userEvent.setup()
    await user.type(screen.getByPlaceholderText(/summarize weekly customer feedback/i), 'Help me')
    await user.click(screen.getByRole('button', { name: /get a proposal/i }))
    expect(await screen.findByText(/create a workflow manually or contact the gallery owner/i)).toBeInTheDocument()
    expect(screen.getByPlaceholderText(/summarize weekly customer feedback/i)).toHaveValue('Help me')
  })

  it('renders the goal input and a disabled submit button when empty', () => {
    renderPanel()

    expect(screen.getByPlaceholderText(/summarize weekly customer feedback/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /get a proposal/i })).toBeDisabled()
  })

  it('submits the goal and renders the proposal on success', async () => {
    const user = userEvent.setup()
    vi.mocked(apiClient.composeWorkflow).mockResolvedValue(sampleResponse)
    renderPanel()

    await user.type(screen.getByPlaceholderText(/summarize weekly customer feedback/i), 'Help me summarize feedback')
    await user.click(screen.getByRole('button', { name: /get a proposal/i }))

    await waitFor(() => expect(screen.getByText('Weekly Customer Pulse')).toBeInTheDocument())
    expect(screen.getByText(/Feedback Summarizer/)).toBeInTheDocument()
    expect(screen.getByText(/no task drafts the final status update/i)).toBeInTheDocument()
    expect(apiClient.composeWorkflow).toHaveBeenCalledWith({ goal: 'Help me summarize feedback' })
  })

  it('shows a rate-limit message with retry time on 429', async () => {
    const user = userEvent.setup()
    vi.mocked(apiClient.composeWorkflow).mockRejectedValue(
      new ApiError(429, 'Too Many Requests', { retryAfterSeconds: 30 }),
    )
    renderPanel()

    await user.type(screen.getByPlaceholderText(/summarize weekly customer feedback/i), 'Help me summarize feedback')
    await user.click(screen.getByRole('button', { name: /get a proposal/i }))

    await waitFor(() => expect(screen.getByText(/try again in about 30 seconds/i)).toBeInTheDocument())
  })

  it('shows a friendly error message on 502', async () => {
    const user = userEvent.setup()
    vi.mocked(apiClient.composeWorkflow).mockRejectedValue(new ApiError(502, 'Bad Gateway'))
    renderPanel()

    await user.type(screen.getByPlaceholderText(/summarize weekly customer feedback/i), 'Help me summarize feedback')
    await user.click(screen.getByRole('button', { name: /get a proposal/i }))

    await waitFor(() => expect(screen.getByText(/could not get a proposal/i)).toBeInTheDocument())
  })

  it('navigates to the workflow editor with a prefill when "Create workflow from this" is clicked', async () => {
    const user = userEvent.setup()
    vi.mocked(apiClient.composeWorkflow).mockResolvedValue(sampleResponse)
    renderPanel()

    await user.type(screen.getByPlaceholderText(/summarize weekly customer feedback/i), 'Help me summarize feedback')
    await user.click(screen.getByRole('button', { name: /get a proposal/i }))
    await waitFor(() => expect(screen.getByText('Weekly Customer Pulse')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: /review and save workflow/i }))

    expect(mockNavigate).toHaveBeenCalledWith(
      '/workflows/new',
      expect.objectContaining({
        state: expect.objectContaining({
          aiComposerPrefill: expect.objectContaining({ name: 'Weekly Customer Pulse' }),
        }),
      }),
    )
  })

  it('navigates to the task editor with a prefill when "Add task" is clicked for a gap', async () => {
    const user = userEvent.setup()
    vi.mocked(apiClient.composeWorkflow).mockResolvedValue(sampleResponse)
    renderPanel()

    await user.type(screen.getByPlaceholderText(/summarize weekly customer feedback/i), 'Help me summarize feedback')
    await user.click(screen.getByRole('button', { name: /get a proposal/i }))
    await waitFor(() => expect(screen.getByText('Weekly Customer Pulse')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: /add task/i }))

    expect(mockNavigate).toHaveBeenCalledWith(
      '/tasks/new',
      expect.objectContaining({
        state: expect.objectContaining({
          taskComposerPrefill: expect.objectContaining({ suggestedName: 'Status Update Drafter' }),
        }),
      }),
    )
  })
})
