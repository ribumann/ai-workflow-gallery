import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { loadBranding } from './scripts/branding.mjs'
import { loadCollectionBranding } from './scripts/collection-branding.mjs'

// Separate from vite.config.ts (T053, issue #53) so the production build's `tsc -b` project
// (tsconfig.app.json only includes "src") never picks up test files under tests/unit/.
export default defineConfig({
  plugins: [react()],
  define: {
    __GALLERY_BRANDING__: JSON.stringify(loadBranding()),
    __GALLERY_COLLECTION_BRANDING__: JSON.stringify(loadCollectionBranding()),
  },
  test: {
    environment: 'jsdom',
    globals: true,
    // Let jsdom provide browser Storage instead of Node 25+'s file-backed global.
    execArgv: Number(process.versions.node.split('.')[0]) >= 25 ? ['--no-webstorage'] : [],
    setupFiles: ['./tests/unit/setup.ts'],
    include: ['tests/unit/**/*.test.{ts,tsx}'],
    server: {
      // Force Vite's own transform pipeline (instead of Node's native ESM/CJS interop) for
      // tabster and @fluentui packages: tabster ships a `.cjs` main entry whose named exports
      // Node's cjs-module-lexer can't statically detect, which otherwise breaks
      // `import { FluentProvider } from '@fluentui/react-components'` under Vitest.
      deps: {
        inline: [/tabster/, /@fluentui\//],
      },
    },
  },
})
