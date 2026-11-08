import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { expect, it, vi } from 'vitest'
import { TargetPicker } from '../../src/components/TargetPicker'

it('searches aliases and supports keyboard selection of a documented target', async () => {
  const onChange = vi.fn()
  render(<FluentProvider theme={webLightTheme}>
    <TargetPicker kind="skill" value={[]} multiple onChange={onChange} />
  </FluentProvider>)
  const input = screen.getByRole('combobox', { name: 'Works in' })
  await userEvent.type(input, 'Codex')
  expect(screen.getByRole('menuitemcheckbox', { name: 'OpenAI Codex CLI' })).toBeVisible()
  expect(screen.queryByRole('menuitemcheckbox', { name: 'Frontier access program' })).not.toBeInTheDocument()
  await userEvent.keyboard('{ArrowDown}{Enter}')
  expect(onChange).toHaveBeenCalledWith(['openai.codex.cli'])
}, 15000)

it('shows a real empty result rather than accepting a freeform ID', async () => {
  const onChange = vi.fn()
  render(<FluentProvider theme={webLightTheme}>
    <TargetPicker kind="plugin" value={[]} onChange={onChange} />
  </FluentProvider>)
  await userEvent.type(screen.getByRole('combobox'), 'unknown-installed-product')
  expect(screen.getByRole('option', { name: 'No documented targets match' })).toHaveAttribute('aria-disabled', 'true')
  await userEvent.keyboard('{Enter}')
  expect(onChange).not.toHaveBeenCalled()
})
