import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { THEME_STORAGE_KEY, ThemeProvider, ThemeSwitch } from './theme'

function mockSystem(dark: boolean) {
  const listeners: (() => void)[] = []
  const mq = {
    matches: dark,
    addEventListener: (_: string, fn: () => void) => listeners.push(fn),
    removeEventListener: () => undefined,
  }
  vi.stubGlobal('matchMedia', () => mq)
  return {
    change(next: boolean) {
      mq.matches = next
      listeners.forEach((l) => l())
    },
  }
}

const renderSwitch = () =>
  render(
    <ThemeProvider>
      <ThemeSwitch />
    </ThemeProvider>,
  )

describe('tema claro/oscuro', () => {
  beforeEach(() => {
    localStorage.clear()
    delete document.documentElement.dataset.theme
  })
  afterEach(() => vi.unstubAllGlobals())

  it('la primera vez sigue la preferencia del sistema y sus cambios', async () => {
    const system = mockSystem(true)
    renderSwitch()
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(screen.getByRole('switch', { name: 'Modo oscuro' })).toHaveAttribute('aria-checked', 'true')

    const { act } = await import('react')
    act(() => system.change(false))
    expect(document.documentElement.dataset.theme).toBe('light')
  })

  it('el switch cambia el tema y lo recuerda en el navegador', async () => {
    mockSystem(false)
    renderSwitch()
    await userEvent.click(screen.getByRole('switch'))
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
  })

  it('la elección guardada tiene prioridad sobre el sistema', () => {
    mockSystem(true)
    localStorage.setItem(THEME_STORAGE_KEY, 'light')
    renderSwitch()
    expect(document.documentElement.dataset.theme).toBe('light')
    expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false')
  })
})
