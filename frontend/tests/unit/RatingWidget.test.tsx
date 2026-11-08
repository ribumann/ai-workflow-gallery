import { render, screen, waitFor } from '@testing-library/react'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { describe, expect, it, vi } from 'vitest'
import { RatingWidget } from '../../src/components/RatingWidget'
import { apiClient } from '../../src/services/apiClient'
import type { RatingSummary } from '../../src/services/types'

// Covers the 👍/👎 rating widget (issue #60): it loads the current aggregate/vote on mount and
// reflects a new vote after clicking. Mocks the API client so this doesn't depend on a live
// backend.
vi.mock('../../src/services/apiClient', () => ({
  apiClient: {
    getRating: vi.fn(),
    rate: vi.fn(),
  },
}))

function renderWidget() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <RatingWidget kind="tasks" id="a1" />
    </FluentProvider>,
  )
}

const noVotes: RatingSummary = { helpfulCount: 0, notHelpfulCount: 0, myVote: null }

describe('RatingWidget', () => {
  it('renders the loaded counts', async () => {
    vi.mocked(apiClient.getRating).mockResolvedValue({
      helpfulCount: 3,
      notHelpfulCount: 1,
      myVote: null,
    })

    renderWidget()

    await waitFor(() => expect(screen.getByText('3')).toBeInTheDocument())
    expect(screen.getByText('1')).toBeInTheDocument()
  })

  it('updates counts after casting a vote', async () => {
    vi.mocked(apiClient.getRating).mockResolvedValue(noVotes)
    vi.mocked(apiClient.rate).mockResolvedValue({
      helpfulCount: 1,
      notHelpfulCount: 0,
      myVote: 1,
    })

    renderWidget()

    await waitFor(() => expect(screen.getByRole('button', { name: 'Helpful' })).toBeInTheDocument())
    screen.getByRole('button', { name: 'Helpful' }).click()

    await waitFor(() => expect(apiClient.rate).toHaveBeenCalledWith('tasks', 'a1', expect.any(String), 1))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Helpful' })).toHaveAttribute('aria-pressed', 'true'))
  })
})
