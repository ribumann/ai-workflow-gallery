import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { describe, expect, it, vi } from 'vitest'
import { MarkdownContentEditor } from '../../src/components/MarkdownContentEditor'
import { MarkdownContent } from '../../src/components/MarkdownContent'

function renderEditor(props: Partial<React.ComponentProps<typeof MarkdownContentEditor>> = {}) {
  const onChange = vi.fn()
  render(
    <FluentProvider theme={webLightTheme}>
      <MarkdownContentEditor
        aria-label="Description"
        aria-invalid="true"
        value=""
        onChange={onChange}
        {...props}
      />
    </FluentProvider>,
  )
  return onChange
}

describe('MarkdownContentEditor', () => {
  it('inserts Markdown formatting around the selected text', async () => {
    const onChange = renderEditor({ value: 'selected text' })
    const editor = screen.getByRole('textbox', { name: 'Description' })
    editor.focus()
    editor.setSelectionRange(0, 'selected'.length)

    await userEvent.click(screen.getByRole('button', { name: 'Bold' }))

    expect(onChange).toHaveBeenCalledWith('**selected** text')
  })

  it('inserts fenced code and list syntax without exceeding the supplied maximum length', async () => {
    const onChange = renderEditor({ value: 'line one\nline two' })
    const editor = screen.getByRole('textbox', { name: 'Description' })
    editor.focus()
    editor.setSelectionRange(0, 'line one\nline two'.length)

    await userEvent.click(screen.getByRole('button', { name: 'Bulleted list' }))
    expect(onChange).toHaveBeenCalledWith('- line one\n- line two')

    cleanup()
    const codeChange = renderEditor({ value: 'line one\nline two' })
    const codeEditor = screen.getByRole('textbox', { name: 'Description' })
    codeEditor.focus()
    codeEditor.setSelectionRange(0, 'line one\nline two'.length)
    await userEvent.click(screen.getByRole('button', { name: 'Code block' }))
    expect(codeChange).toHaveBeenCalledWith('```\nline one\nline two\n```')

    cleanup()
    const constrained = renderEditor({ maxLength: 3 })
    await userEvent.click(screen.getByRole('button', { name: 'Bold' }))
    expect(constrained).not.toHaveBeenCalled()
  })

  it('preserves validation attributes and renders sanitized code previews', async () => {
    const { container } = render(
      <FluentProvider theme={webLightTheme}>
        <MarkdownContentEditor
          aria-label="Description"
          aria-invalid="true"
          value={'```ts\nconst safe = true\n```\n\n<script>alert(1)</script>'}
          onChange={() => {}}
        />
      </FluentProvider>,
    )

    expect(screen.getByRole('textbox', { name: 'Description' })).toHaveAttribute(
      'aria-invalid',
      'true',
    )
    await userEvent.click(screen.getByRole('tab', { name: 'Preview' }))
    expect(screen.getByText('const safe = true')).toBeInTheDocument()
    expect(container.querySelector('script')).toBeNull()
  })

  it('renders stored Markdown safely in read views', () => {
    const { container } = render(
      <FluentProvider theme={webLightTheme}>
        <MarkdownContent
          content={'# Heading\n\n```json\n{\"safe\": true}\n```\n\n[bad](javascript:alert(1))'}
        />
      </FluentProvider>,
    )

    expect(screen.getByRole('heading', { name: 'Heading' })).toBeInTheDocument()
    expect(screen.getByText('{"safe": true}')).toBeInTheDocument()
    expect(container.querySelector('a[href^="javascript:"]')).toBeNull()
  })
})
