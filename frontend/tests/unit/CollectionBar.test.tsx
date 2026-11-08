import { screen } from '@testing-library/react'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { CollectionBar } from '../../src/components/CollectionBar'
import { renderWithAuth } from './authTestUtils'

const mode = vi.hoisted(() => ({ enabled: false }))
vi.mock('../../src/services/deployment', async (importOriginal) => ({
  ...await importOriginal<typeof import('../../src/services/deployment')>(),
  get publicDemo() { return mode.enabled },
}))

describe('CollectionBar', () => {
  beforeEach(() => {
    window.sessionStorage.clear()
  })

  it.each([false, true])('keeps collection access clear without promotional copy (public=%s)', (publicDemo) => {
    mode.enabled = publicDemo
    renderWithAuth(<FluentProvider theme={webLightTheme}><CollectionBar /></FluentProvider>,
      { authState: { publicDemo } })
    const region = screen.getByRole('region', { name: 'Selected collection' })
    expect(screen.getByRole('combobox', { name: 'Collection' })).toBeInTheDocument()
    expect(region).not.toHaveTextContent('Original examples based on public documentation.')
    if (publicDemo) expect(region).toHaveTextContent('Published content is read-only.')
    expect(region).not.toHaveTextContent(/Composer|anonymous visitors/)
  })

  it('retains the Playground privacy warning', () => {
    mode.enabled = true
    window.sessionStorage.setItem('gallery.collection', 'playground')
    renderWithAuth(<FluentProvider theme={webLightTheme}><CollectionBar /></FluentProvider>,
      { authState: { publicDemo: true } })
    const region = screen.getByRole('region', { name: 'Selected collection' })
    expect(region).toHaveTextContent('Never store sensitive or private data.')
    expect(region).toHaveTextContent('Playground content is shared, public and disposable.')
  })
})
