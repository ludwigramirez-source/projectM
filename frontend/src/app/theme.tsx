import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Moon, Sun } from 'lucide-react'
import { cn } from '@/lib/cn'

export type Theme = 'light' | 'dark'

export const THEME_STORAGE_KEY = 'pm-theme'

function readStored(): Theme | null {
  try {
    const v = localStorage.getItem(THEME_STORAGE_KEY)
    return v === 'light' || v === 'dark' ? v : null
  } catch {
    return null
  }
}

function systemTheme(): Theme {
  return typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export function applyTheme(theme: Theme) {
  const root = document.documentElement
  root.dataset.theme = theme
  // La barra del navegador / PWA toma el color de la barra lateral del tema activo.
  const sidebar = getComputedStyle(root).getPropertyValue('--pm-sidebar-bg').trim()
  if (sidebar) document.querySelector('meta[name="theme-color"]')?.setAttribute('content', `rgb(${sidebar})`)
}

interface ThemeState {
  theme: Theme
  toggle: () => void
  setTheme: (t: Theme) => void
}

const ThemeCtx = createContext<ThemeState | null>(null)

/**
 * Tema claro/oscuro. La primera vez sigue al sistema operativo (y sus cambios); en cuanto el
 * usuario elige con el switch, se recuerda en este navegador.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [chosen, setChosen] = useState<Theme | null>(readStored)
  const [system, setSystem] = useState<Theme>(systemTheme)
  const theme = chosen ?? system

  useEffect(() => {
    if (typeof matchMedia !== 'function') return
    const mq = matchMedia('(prefers-color-scheme: dark)')
    const onChange = () => setSystem(mq.matches ? 'dark' : 'light')
    mq.addEventListener('change', onChange)
    return () => mq.removeEventListener('change', onChange)
  }, [])

  useEffect(() => {
    applyTheme(theme)
    // Al imprimir (p. ej. el acta de cierre) siempre se usa el tema claro.
    const before = () => applyTheme('light')
    const after = () => applyTheme(theme)
    window.addEventListener('beforeprint', before)
    window.addEventListener('afterprint', after)
    return () => {
      window.removeEventListener('beforeprint', before)
      window.removeEventListener('afterprint', after)
    }
  }, [theme])

  const setTheme = useCallback((t: Theme) => {
    setChosen(t)
    try {
      localStorage.setItem(THEME_STORAGE_KEY, t)
    } catch {
      // Sin almacenamiento (modo privado): el tema vale solo para esta sesión.
    }
  }, [])
  const toggle = useCallback(() => setTheme(theme === 'dark' ? 'light' : 'dark'), [theme, setTheme])

  const value = useMemo(() => ({ theme, toggle, setTheme }), [theme, toggle, setTheme])
  return <ThemeCtx.Provider value={value}>{children}</ThemeCtx.Provider>
}

export function useTheme() {
  const ctx = useContext(ThemeCtx)
  if (!ctx) throw new Error('useTheme fuera de ThemeProvider')
  return ctx
}

/** Switch accesible de modo oscuro (sol/luna). */
export function ThemeSwitch({ className }: { className?: string }) {
  const { theme, toggle } = useTheme()
  const dark = theme === 'dark'
  return (
    <button
      type="button"
      role="switch"
      aria-checked={dark}
      aria-label="Modo oscuro"
      title={dark ? 'Cambiar a modo claro' : 'Cambiar a modo oscuro'}
      onClick={toggle}
      className={cn(
        'inline-flex h-7 w-12 shrink-0 items-center rounded-full border border-border bg-surface-muted transition-colors',
        className,
      )}
    >
      <span
        aria-hidden
        className={cn(
          'flex size-5 items-center justify-center rounded-full bg-surface shadow transition-transform',
          dark ? 'translate-x-6 text-primary-fg' : 'translate-x-1 text-accent-fg',
        )}
      >
        {dark ? <Moon className="size-3.5" /> : <Sun className="size-3.5" />}
      </span>
    </button>
  )
}
