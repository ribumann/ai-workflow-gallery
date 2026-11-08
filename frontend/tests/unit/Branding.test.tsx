import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FluentProvider } from '@fluentui/react-components'
import { describe, expect, it } from 'vitest'
import { branding, createGalleryTheme, galleryTheme, type Branding } from '../../src/branding'
import { ShareMenu } from '../../src/components/ShareMenu'

function luminance(hex: string) {
  const [r, g, b] = [1, 3, 5].map((offset) => {
    const value = parseInt(hex.slice(offset, offset + 2), 16) / 255
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  })
  return r * 0.2126 + g * 0.7152 + b * 0.0722
}

describe('configured Fluent branding', () => {
  it('projects independent collection presets through the same theme factory without changing defaults', () => {
    const presets: Branding[] = [
      { ...branding, title: 'Public Techniques', accent: '#0F6CBD' },
      { ...branding, title: 'Selected Tools', accent: '#6B246E' },
    ]
    const original = JSON.stringify(branding)
    const themes = presets.map(createGalleryTheme)
    for (const [index, theme] of themes.entries()) {
      expect(theme.colorBrandBackground.toLowerCase()).toBe(presets[index].accent!.toLowerCase())
      expect(1.05 / (luminance(theme.colorBrandBackground) + 0.05)).toBeGreaterThanOrEqual(4.5)
      expect(theme.colorNeutralBackground1).toBe(galleryTheme.colorNeutralBackground1)
    }
    expect(themes[0].colorBrandBackground).not.toBe(themes[1].colorBrandBackground)
    expect(createGalleryTheme({ ...branding, accent: undefined }).colorBrandBackground).toBe('#0f6cbd')
    expect(JSON.stringify(branding)).toBe(original)
  })

  it('keeps actual primary/hover and branded text tokens accessible on their surfaces', () => {
    for (const token of [
      'colorBrandBackground',
      'colorBrandBackgroundHover',
      'colorBrandForeground1',
      'colorBrandForegroundLink',
    ] as const) {
      expect(1.05 / (luminance(galleryTheme[token]) + 0.05)).toBeGreaterThanOrEqual(4.5)
    }
    if (branding.accent)
      expect(galleryTheme.colorBrandBackground.toLowerCase()).toBe(branding.accent.toLowerCase())
  })

  it('shows only configured share channels without invoking a share', async () => {
    const user = userEvent.setup()
    render(
      <FluentProvider theme={galleryTheme}>
        <ShareMenu title="Example task" />
      </FluentProvider>,
    )
    await user.click(screen.getByRole('button', { name: 'Share', exact: true }))
    for (const [channel, name] of [
      ['copy', 'Copy link'],
      ['email', 'Share via email'],
      ['teams', 'Share to Teams'],
    ] as const) {
      if (branding.shareChannels.includes(channel)) {
        expect(screen.getByRole('menuitem', { name })).toBeVisible()
      } else {
        expect(screen.queryByRole('menuitem', { name })).not.toBeInTheDocument()
      }
    }
  })
})
