// @vitest-environment node
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { contrastRatio, type Rgb } from './contrast'

const css = readFileSync(fileURLToPath(new URL('./tokens.css', import.meta.url)), 'utf8')
const darkStart = css.indexOf("[data-theme='dark']")
const rootBlock = css.slice(0, darkStart)
const darkBlock = css.slice(darkStart)

type Raw = Map<string, string>
function parse(block: string): Raw {
  const m = new Map<string, string>()
  for (const x of block.matchAll(/--pm-([a-z0-9-]+):\s*([^;]+);/g)) m.set(x[1]!, x[2]!.trim())
  return m
}
const root = parse(rootBlock)
const dark = new Map([...root, ...parse(darkBlock)])

/** Resuelve un token (siguiendo var(--pm-x)) a canales RGB. */
function resolve(theme: Raw, name: string, depth = 0): Rgb {
  const raw = theme.get(name)
  if (!raw || depth > 10) throw new Error(`Token inexistente: ${name}`)
  const ref = /^var\(--pm-([a-z0-9-]+)\)$/.exec(raw)
  if (ref) return resolve(theme, ref[1]!, depth + 1)
  const parts = raw.split(/\s+/).map(Number)
  if (parts.length !== 3 || parts.some(Number.isNaN)) throw new Error(`Valor inválido en ${name}: ${raw}`)
  return parts as Rgb
}

const themes = { claro: root, oscuro: dark } as const
const TONES = ['primary', 'secondary', 'accent', 'success', 'danger', 'neutral']

describe('paleta obligatoria', () => {
  it.each([
    ['primary-500', [0x5d, 0xb1, 0xda]],
    ['secondary-500', [0x6f, 0x7e, 0xa4]],
    ['accent-500', [0x46, 0xf7, 0xbe]],
    ['success-500', [0x65, 0xd0, 0xaf]],
    ['danger-500', [0xb1, 0x06, 0x0d]],
  ])('%s usa el color base exacto', (name, rgb) => {
    expect(resolve(root, name)).toEqual(rgb)
  })
})

/** Pares texto/fondo (AA texto normal = 4.5:1) que deben cumplirse en cada tema. */
const TEXT_PAIRS: [fg: string, bg: string][] = [
  ['fg', 'bg'],
  ['fg', 'surface'],
  ['fg', 'surface-muted'],
  ['fg-muted', 'bg'],
  ['fg-muted', 'surface'],
  ['fg-muted', 'surface-muted'],
  ['sidebar-fg', 'sidebar-bg'],
  ['sidebar-fg-muted', 'sidebar-bg'],
  ['sidebar-fg', 'sidebar-active'],
  ...TONES.flatMap((t): [string, string][] => [
    [`${t}-subtle-fg`, `${t}-subtle`],
    [`${t}-fg`, 'surface'],
    [`${t}-fg`, 'bg'],
    [`on-${t}-solid`, `${t}-solid`],
    [`on-${t}-solid`, `${t}-solid-hover`],
  ]),
]

describe.each(Object.entries(themes))('contraste WCAG AA en modo %s', (_name, theme) => {
  it.each(TEXT_PAIRS)('%s sobre %s', (fg, bg) => {
    expect(contrastRatio(resolve(theme, fg), resolve(theme, bg))).toBeGreaterThanOrEqual(4.5)
  })
})

describe('marcas de gráficos en modo oscuro (≥ 3:1 contra la superficie)', () => {
  it.each(TONES)('%s-mark', (t) => {
    expect(contrastRatio(resolve(dark, `${t}-mark`), resolve(dark, 'surface'))).toBeGreaterThanOrEqual(3)
  })
})

describe('combinaciones prohibidas (documentan la regla de la paleta)', () => {
  it.each(['primary-500', 'accent-500', 'success-500'])('texto blanco sobre %s NO cumple AA', (bg) => {
    expect(contrastRatio(resolve(root, 'white'), resolve(root, bg))).toBeLessThan(4.5)
  })
})
