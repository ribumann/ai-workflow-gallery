import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { describe, expect, it, vi } from 'vitest'
import { CommentsSection } from '../../src/components/CommentsSection'
import { apiClient } from '../../src/services/apiClient'

// Covers the comments list + add form (issue #60): it lists existing comments newest-first and
// appends a newly posted one to the top. Mocks the API client so this doesn't depend on a live
// backend.
vi.mock('../../src/services/apiClient', () => ({
  apiClient: {
    listComments: vi.fn(),
    addComment: vi.fn(),
  },
}))

function renderSection() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <CommentsSection kind="tasks" id="a1" />
    </FluentProvider>,
  )
}

describe('CommentsSection', () => {
  it('shows an empty state when there are no comments', async () => {
    vi.mocked(apiClient.listComments).mockResolvedValue({ items: [] })

    renderSection()

    await waitFor(() =>
      expect(screen.getByText('No comments yet. Be the first to share your thoughts.')).toBeInTheDocument(),
    )
  })

  it('renders existing comments', async () => {
    vi.mocked(apiClient.listComments).mockResolvedValue({
      items: [
        {
          id: 'c1',
          body: 'This was really helpful, thanks!',
          createdByDisplayName: 'Anonymous contributor',
          createdAt: new Date().toISOString(),
        },
      ],
    })

    renderSection()

    await waitFor(() => expect(screen.getByText('This was really helpful, thanks!')).toBeInTheDocument())
  })

  it('posts a new comment and prepends it to the list', async () => {
    vi.mocked(apiClient.listComments).mockResolvedValue({ items: [] })
    vi.mocked(apiClient.addComment).mockResolvedValue({
      id: 'c2',
      body: 'Great addition to the catalog.',
      createdByDisplayName: 'Anonymous contributor',
      createdAt: new Date().toISOString(),
    })

    renderSection()

    await waitFor(() =>
      expect(screen.getByText('No comments yet. Be the first to share your thoughts.')).toBeInTheDocument(),
    )

    const textarea = screen.getByPlaceholderText('Share your experience with this...')
    fireEvent.change(textarea, { target: { value: 'Great addition to the catalog.' } })
    screen.getByRole('button', { name: 'Post comment' }).click()

    await waitFor(() => expect(apiClient.addComment).toHaveBeenCalledWith('tasks', 'a1', 'Great addition to the catalog.'))
    await waitFor(() => expect(screen.getByText('Great addition to the catalog.')).toBeInTheDocument())
  })
})
