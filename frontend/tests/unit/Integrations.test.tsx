import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { describe, expect, it } from 'vitest'
import { IntegrationGuidePage } from '../../src/pages/Integrations/IntegrationGuidePage'
import { integrationGuides } from '../../src/pages/Integrations/integrationGuides'
import { branding } from '../../src/branding'

function renderGuide(path: string) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/integrations/:id" element={<IntegrationGuidePage />} />
          <Route path="*" element={<div>redirected</div>} />
        </Routes>
      </MemoryRouter>
    </FluentProvider>,
  )
}

describe('Tool integration guides', () => {
  it('publishes one guide per supported surface', () => {
    expect(integrationGuides.map((guide) => guide.id)).toEqual([
      'github-copilot',
      'cowork',
      'scout',
    ])
  })

  it('puts readiness and prerequisites before manual self-install instructions', () => {
    for (const path of ['/integrations/github-copilot', '/integrations/cowork', '/integrations/scout']) {
      const { unmount } = renderGuide(path)
      const headings = screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)
      expect(headings.slice(0, 3)).toEqual([
        'Check deployment readiness first',
        'Prerequisites for an existing enabled MCP server',
        'Copy/paste self-install instruction',
      ])
      expect(screen.getAllByText(new RegExp(`${window.location.origin}${path}`)).length).toBeGreaterThan(0)
      unmount()
    }
  })

  it('renders the GitHub Copilot guide with setup steps and starter prompts', () => {
    renderGuide('/integrations/github-copilot')

    expect(screen.getByRole('heading', { level: 1, name: /GitHub Copilot/ })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Prerequisites for an existing enabled MCP server/ })).toBeInTheDocument()
    expect(
      screen.getByRole('heading', { name: /Copy\/paste the reference-client verification/ }),
    ).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Starter prompts/ })).toBeInTheDocument()

    const prompts = screen.getAllByRole('button', { name: /^Copy prompt:/ })
    expect(prompts.length).toBeGreaterThanOrEqual(4)
  })

  it('derives the MCP endpoint from the origin the app is served from', () => {
    const { container } = renderGuide('/integrations/github-copilot')
    expect(within(container).getAllByText(`${window.location.origin}/api/mcp`).length).toBeGreaterThan(0)
  })

  it('offers the skill download on the GitHub Copilot guide', () => {
    renderGuide('/integrations/github-copilot')
    expect(screen.getByRole('heading', { name: /Teach your assistant when to use the gallery/ })).toBeInTheDocument()
    const link = screen.getAllByRole('link', { name: /skill folder/ })[0]
    expect(link).toHaveAttribute('href', '/downloads/ai-workflow-gallery-skill.zip')
  })

  it('presents Cowork plugins as policy-dependent with an always-available browser fallback', () => {
    renderGuide('/integrations/cowork')
    expect(screen.getByRole('heading', { name: /Option A — Review the Cowork plugin package/ })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Option B — Use the authorized browser \(always available\)/ })).toBeInTheDocument()
    expect(screen.getByText(/The owner must supply a publisher identity/)).toBeInTheDocument()
    expect(screen.getByText(/remains available when plugin import is unavailable or blocked by organizational policy/)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /MCP connector compatibility is not yet verified/ })).toBeInTheDocument()
    if (branding.publisher) {
      expect(screen.getByRole('link', { name: /Cowork plugin package/ })).toHaveAttribute(
        'href', '/downloads/ai-workflow-gallery-cowork-plugin.zip',
      )
    } else {
      expect(screen.queryByRole('link', { name: /Cowork plugin package/ })).not.toBeInTheDocument()
    }
  })

  it('does not equate Scout custom headers with OAuth compatibility', () => {
    renderGuide('/integrations/scout')
    expect(screen.getAllByText(/Client compatibility unverified/).length).toBeGreaterThan(0)
    expect(screen.getByRole('heading', { name: /Prerequisites for an existing enabled MCP server/ })).toBeInTheDocument()
    expect(screen.queryByText(/StaticWebAppsAuthCookie/)).not.toBeInTheDocument()
  })

  it('redirects an unknown integration id to the GitHub Copilot guide', () => {
    renderGuide('/integrations/does-not-exist')
    expect(screen.getByRole('heading', { level: 1, name: /GitHub Copilot/ })).toBeInTheDocument()
  })
})
