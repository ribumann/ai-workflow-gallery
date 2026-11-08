import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { describe, expect, it, vi } from 'vitest'
import { FeedbackDialog } from '../../src/components/FeedbackDialog'
import { apiClient, ApiError } from '../../src/services/apiClient'

vi.mock('../../src/services/apiClient', async () => ({
  ...await vi.importActual<typeof import('../../src/services/apiClient')>('../../src/services/apiClient'),
  apiClient: { submitFeedback: vi.fn() },
}))

async function openAndSubmit() {
  render(<FluentProvider theme={webLightTheme}><MemoryRouter><FeedbackDialog /></MemoryRouter></FluentProvider>)
  const user = userEvent.setup()
  await user.click(screen.getByRole('button', { name: /give feedback/i }))
  await user.type(await screen.findByLabelText("What's on your mind?"), 'My feedback')
  await user.click(screen.getByRole('button', { name: /submit feedback/i }))
}

describe('FeedbackDialog', () => {
  it('explains disabled delivery and preserves the message', async () => {
    vi.mocked(apiClient.submitFeedback).mockRejectedValue(new ApiError(503, 'Unavailable', { error: 'feedback_disabled' }))
    await openAndSubmit()
    expect(await screen.findByText(/feedback delivery is not available in this environment/i)).toBeInTheDocument()
    expect(screen.getByLabelText("What's on your mind?")).toHaveValue('My feedback')
  })

  it('does not claim mailbox delivery for an accepted operation', async () => {
    vi.mocked(apiClient.submitFeedback).mockResolvedValue({ operationId: 'operation-42' })
    await openAndSubmit()
    expect(await screen.findByText(/queued for delivery.*mailbox delivery is not yet confirmed/i)).toBeInTheDocument()
  })

  it('warns against duplicate sends when queuing is uncertain', async () => {
    vi.mocked(apiClient.submitFeedback).mockRejectedValue(new ApiError(502, 'Bad Gateway'))
    await openAndSubmit()
    expect(await screen.findByText(/contact the gallery owner before retrying/i)).toBeInTheDocument()
    expect(screen.getByLabelText("What's on your mind?")).toHaveValue('My feedback')
  })
})
