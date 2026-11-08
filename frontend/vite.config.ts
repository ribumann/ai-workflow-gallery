import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { brandingHtml, loadBranding } from './scripts/branding.mjs'
import { loadCollectionBranding } from './scripts/collection-branding.mjs'

const branding = loadBranding()

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), {
    name: 'gallery-branding',
    transformIndexHtml: (html) => brandingHtml(branding, html),
  }],
  define: {
    __GALLERY_BRANDING__: JSON.stringify(branding),
    __GALLERY_COLLECTION_BRANDING__: JSON.stringify(loadCollectionBranding()),
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': 'http://localhost:7071',
    },
  },
})
