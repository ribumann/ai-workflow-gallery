import { createLightTheme, webLightTheme, type BrandVariants } from '@fluentui/react-components'
import type { Branding } from '../scripts/branding.mjs'
import { publicDemo, selectedCollection } from './services/deployment'
export type { Branding } from '../scripts/branding.mjs'

declare const __GALLERY_BRANDING__: Branding
declare const __GALLERY_COLLECTION_BRANDING__: Record<string, Branding>
export const branding = publicDemo ? __GALLERY_COLLECTION_BRANDING__[selectedCollection()] : __GALLERY_BRANDING__

export function createGalleryTheme(brand: Branding) {
  const { accent } = brand
  if (!accent) return webLightTheme
  const rgb = [1, 3, 5].map((offset) => parseInt(accent.slice(offset, offset + 2), 16))
  const ramp = {} as BrandVariants
  for (let step = 10; step <= 160; step += 10) {
    const color = rgb.map((v) =>
      step <= 80 ? (v * step) / 80 : v + ((255 - v) * (step - 80)) / 85,
    )
    ramp[step as keyof BrandVariants] =
      `#${color.map((v) => Math.round(v).toString(16).padStart(2, '0')).join('')}`
  }
  return createLightTheme(ramp)
}

export const galleryTheme = createGalleryTheme(branding)
