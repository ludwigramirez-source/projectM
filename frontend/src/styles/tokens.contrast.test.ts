// @vitest-environment node
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { contrastRatio, type Rgb } from './contrast'

const css = readFileSync(fileURLToPath(new URL('./tokens.css', import.meta.url)), 'utf8')
const root = css.slice(0, css.indexOf("[data-theme='dark']"))

/** Lee los tokens "--pm-x-500: r g b;" del bloque :root. */
const tokens = new Map<string, Rgb>()
for (const m of root.matchAll(/--pm-([a-z-]+?\d*|white): (\d+) (\d+) (\d+);/g)) {
  tokens.set(m[1], [Number(m[2]), Number(m[3]), Number(m[4])])
}
const t = (name: string): Rgb => {
  const v = tokens.get(name)
  if (!v) throw new Error(`Token inexistente: ${name}`)
  return v
}

describe('paleta obligatoria', () => {
  it.each([
    ['primary-500', [0x5d, 0xb1, 0xda]],
    ['secondary-500', [0x6f, 0x7e, 0xa4]],
    ['accent-500', [0x46, 0xf7, 0xbe]],
    ['success-500', [0x65, 0xd0, 0xaf]],
    ['danger-500', [0xb1, 0x06, 0x0d]],
  ])('%s usa el color base exacto', (name, rgb) => {
    expect(t(name)).toEqual(rgb)
  })
})

/** Pares texto/fondo que la UI usa. AA texto normal = 4.5:1 */
const AA_PAIRS: [fg: string, bg: string][] = [
  // Botones con texto blanco
  ['white', 'primary-700'],
  ['white', 'primary-800'],
  ['white', 'success-700'],
  ['white', 'danger-500'],
  ['white', 'danger-600'],
  ['white', 'secondary-700'],
  // Sidebar
  ['white', 'secondary-800'],
  ['secondary-100', 'secondary-800'],
  ['white', 'secondary-700'],
  // Texto oscuro sobre colores claros de la marca
  ['neutral-900', 'primary-500'],
  ['neutral-900', 'accent-500'],
  ['neutral-900', 'success-500'],
  ['neutral-900', 'accent-400'],
  // Badges de estado
  ['secondary-800', 'secondary-100'],
  ['primary-800', 'primary-100'],
  ['accent-900', 'accent-100'],
  ['success-800', 'success-100'],
  ['danger-700', 'danger-50'],
  // Texto de la interfaz
  ['neutral-900', 'neutral-50'],
  ['neutral-900', 'white'],
  ['neutral-600', 'white'],
  ['neutral-600', 'neutral-50'],
  ['neutral-600', 'neutral-100'],
  ['primary-700', 'white'],
  ['accent-800', 'white'],
  ['success-700', 'white'],
  ['danger-500', 'white'],
]

describe('contraste WCAG AA (≥ 4.5:1)', () => {
  it.each(AA_PAIRS)('%s sobre %s', (fg, bg) => {
    expect(contrastRatio(t(fg), t(bg))).toBeGreaterThanOrEqual(4.5)
  })
})

describe('combinaciones prohibidas (documentan la regla de la paleta)', () => {
  it.each(['primary-500', 'accent-500', 'success-500'])('texto blanco sobre %s NO cumple AA', (bg) => {
    expect(contrastRatio(t('white'), t(bg))).toBeLessThan(4.5)
  })
})
