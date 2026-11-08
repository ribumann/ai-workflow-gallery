import { render, screen } from '@testing-library/react'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { describe, expect, it } from 'vitest'
import { MarkdownExcerpt } from '../../src/components/MarkdownExcerpt'

function renderExcerpt(content: string | null | undefined, lines?: number) {
  const { container } = render(
    <FluentProvider theme={webLightTheme}>
      <MarkdownExcerpt content={content} lines={lines} />
    </FluentProvider>,
  )
  // The excerpt is the only element the component renders inside the provider.
  return container.querySelector('div[style]') as HTMLElement | null
}

describe('MarkdownExcerpt', () => {
  it('renders emphasis instead of leaking raw Markdown syntax into overviews', () => {
    renderExcerpt('A **bold** claim and some `code`.')

    expect(screen.getByText('bold').tagName).toBe('STRONG')
    expect(screen.getByText('code').tagName).toBe('CODE')
    expect(screen.queryByText(/\*\*bold\*\*/)).toBeNull()
  })

  it('keeps block content readable as one run of prose', () => {
    const excerpt = renderExcerpt('# Heading\n\nFirst para\n\n- one\n- two')

    // Blocks are flattened, so their text must stay word-separated rather than running together.
    // Collapse whitespace the way `white-space: normal` renders it before asserting.
    const rendered = excerpt?.textContent?.replace(/\s+/g, ' ').trim()
    expect(rendered).toContain('Heading First para')
    expect(rendered).toContain('one two')
  })

  it('turns a hard line break into a space rather than joining the words', () => {
    const excerpt = renderExcerpt('line one  \nline two')

    expect(excerpt?.querySelector('br')).toBeNull()
    expect(excerpt?.textContent?.replace(/\s+/g, ' ').trim()).toBe('line one line two')
  })

  it('strips links and images so a clickable row never nests an interactive element', () => {
    renderExcerpt('See [the docs](https://example.com) and ![alt](https://example.com/x.png)')

    expect(screen.queryByRole('link')).toBeNull()
    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByText(/the docs/)).toBeTruthy()
  })

  it('clamps to the requested number of lines', () => {
    const excerpt = renderExcerpt('Some long description', 3)

    expect(excerpt?.style.webkitLineClamp).toBe('3')
  })

  it('renders nothing for empty or missing content', () => {
    expect(renderExcerpt('   ')).toBeNull()
    expect(renderExcerpt(null)).toBeNull()
    expect(renderExcerpt(undefined)).toBeNull()
  })
})
