import { defineConfig, minimal2023Preset } from '@vite-pwa/assets-generator/config'

// Genera los íconos de la PWA a partir de public/pwa-icon.svg:
//   npx pwa-assets-generator
export default defineConfig({
  headLinkOptions: { preset: '2023' },
  preset: {
    ...minimal2023Preset,
    maskable: { ...minimal2023Preset.maskable, resizeOptions: { background: '#3B4563' } },
    apple: { ...minimal2023Preset.apple, resizeOptions: { background: '#3B4563' } },
  },
  images: ['public/pwa-icon.svg'],
})
