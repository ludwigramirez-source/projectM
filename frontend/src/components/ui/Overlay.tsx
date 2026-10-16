import { useEffect, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import { cn } from '@/lib/cn'

function useEscape(onClose: () => void, open: boolean) {
  useEffect(() => {
    if (!open) return
    const h = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    document.addEventListener('keydown', h)
    return () => document.removeEventListener('keydown', h)
  }, [onClose, open])
}

interface OverlayProps {
  open: boolean
  onClose: () => void
  title: ReactNode
  children: ReactNode
  footer?: ReactNode
  size?: 'sm' | 'md' | 'lg'
}

export function Modal({ open, onClose, title, children, footer, size = 'md' }: OverlayProps) {
  useEscape(onClose, open)
  if (!open) return null
  const w = { sm: 'max-w-sm', md: 'max-w-lg', lg: 'max-w-3xl' }[size]
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end justify-center overflow-y-auto bg-overlay/50 sm:items-start sm:p-4 sm:pt-16">
      <div className="fixed inset-0" onClick={onClose} aria-hidden />
      <div
        role="dialog"
        aria-modal="true"
        aria-label={typeof title === 'string' ? title : undefined}
        className={cn('relative flex max-h-[92dvh] w-full flex-col rounded-t-xl bg-surface shadow-xl sm:max-h-none sm:rounded-lg', w)}
      >
        <header className="flex items-center justify-between border-b border-border px-5 py-3">
          <h2 className="text-base font-semibold">{title}</h2>
          <button
            onClick={onClose}
            className="rounded p-1 text-fg-muted hover:bg-surface-muted touch:p-2.5"
            aria-label="Cerrar"
          >
            <X className="size-4" />
          </button>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">{children}</div>
        {footer && <footer className="flex justify-end gap-2 border-t border-border px-5 py-3 pb-safe">{footer}</footer>}
      </div>
    </div>,
    document.body,
  )
}

export function Drawer({ open, onClose, title, children, footer }: OverlayProps) {
  useEscape(onClose, open)
  if (!open) return null
  return createPortal(
    <div className="fixed inset-0 z-40 flex justify-end bg-overlay/30">
      <div className="fixed inset-0" onClick={onClose} aria-hidden />
      <aside role="dialog" aria-modal="true" className="relative flex h-dvh w-full max-w-2xl flex-col bg-surface shadow-2xl">
        <header className="flex items-center justify-between gap-2 border-b border-border px-4 py-3 pt-safe sm:px-5">
          <div className="min-w-0 flex-1">{title}</div>
          <button
            onClick={onClose}
            className="rounded p-1 text-fg-muted hover:bg-surface-muted touch:p-2.5"
            aria-label="Cerrar"
          >
            <X className="size-4" />
          </button>
        </header>
        <div className="flex-1 overflow-y-auto px-4 py-4 pb-safe sm:px-5">{children}</div>
        {footer && <footer className="border-t border-border px-5 py-3">{footer}</footer>}
      </aside>
    </div>,
    document.body,
  )
}
