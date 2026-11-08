import { render, screen, waitFor } from '@testing-library/react'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { describe, expect, it, vi } from 'vitest'
import { CaughtYouToast } from '../../src/components/CaughtYouToast'

// Covers the "well tried, caught you!" easter egg toast (user request): it should render only
// while `visible`, and auto-dismiss itself via `onHide` shortly after appearing.
describe('CaughtYouToast', () => {
  function renderToast(visible: boolean, onHide: () => void) {
    return render(
      <FluentProvider theme={webLightTheme}>
        <CaughtYouToast visible={visible} onHide={onHide} />
      </FluentProvider>,
    )
  }

  it('renders nothing when not visible', () => {
    renderToast(false, vi.fn())
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('shows the gotcha message when visible', () => {
    renderToast(true, vi.fn())
    expect(screen.getByText(/caught you/i)).toBeInTheDocument()
  })

  it('auto-dismisses by calling onHide after a short delay', async () => {
    const onHide = vi.fn()
    renderToast(true, onHide)
    await waitFor(() => expect(onHide).toHaveBeenCalled(), { timeout: 5000 })
  })
})
