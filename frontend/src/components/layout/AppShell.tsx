import { useEffect, useState, type ReactNode } from 'react'
import { Menu } from 'lucide-react'
import { cn } from '@/lib/cn'

/** Estructura principal: sidebar (secondary-800) + topbar + contenido. Responsive con sidebar deslizable. */
export function AppShell({ sidebar, topbar, children }: { sidebar: ReactNode; topbar: ReactNode; children: ReactNode }) {
  const [open, setOpen] = useState(false)
  useEffect(() => {
    const mq = matchMedia('(min-width: 1024px)')
    const close = () => mq.matches && setOpen(false)
    mq.addEventListener('change', close)
    return () => mq.removeEventListener('change', close)
  }, [])
  return (
    <div className="flex h-full">
      {open && <div className="fixed inset-0 z-30 bg-overlay/40 lg:hidden" onClick={() => setOpen(false)} aria-hidden />}
      <aside
        className={cn(
          'fixed inset-y-0 left-0 z-40 flex w-72 max-w-[85vw] flex-col bg-sidebar pt-safe pb-safe lg:w-64 text-sidebar-fg transition-transform lg:static lg:translate-x-0',
          open ? 'translate-x-0' : '-translate-x-full',
        )}
        onClick={(e) => (e.target as HTMLElement).closest('a') && setOpen(false)}
      >
        {sidebar}
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 shrink-0 items-center gap-2 border-b border-border bg-surface px-3 pt-safe box-content sm:px-4">
          <button
            className="rounded p-1.5 text-fg-muted hover:bg-surface-muted touch:p-2.5 lg:hidden"
            onClick={() => setOpen(true)}
            aria-label="Abrir menú"
          >
            <Menu className="size-5" />
          </button>
          {topbar}
        </header>
        <main className="min-h-0 flex-1 overflow-y-auto">{children}</main>
      </div>
    </div>
  )
}

export function Logo() {
  return (
    <div className="flex items-center gap-2 px-4 py-4">
      <span className="flex size-8 items-center justify-center rounded-md bg-accent-solid text-sm font-bold text-on-accent-solid">
        PM
      </span>
      <span className="text-base font-semibold tracking-tight">ProjectM</span>
    </div>
  )
}
