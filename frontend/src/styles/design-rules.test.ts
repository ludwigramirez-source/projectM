// @vitest-environment node
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const SRC = fileURLToPath(new URL('..', import.meta.url))

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) return name === 'styles' || name === 'test' ? [] : walk(full)
    return /\.(tsx?|css)$/.test(name) && !name.endsWith('.test.ts') && !name.endsWith('.test.tsx') ? [full] : []
  })
}

describe('reglas del sistema de diseño', () => {
  it('ningún componente usa colores hex/rgb sueltos (solo tokens)', () => {
    const offenders = walk(SRC)
      .filter((f) => !f.endsWith('index.css'))
      .filter((f) => /#[0-9a-fA-F]{3,8}\b(?![\w-])|rgba?\(\s*\d/.test(readFileSync(f, 'utf8')))
    expect(offenders).toEqual([])
  })
})
