import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { AlertTriangle, CheckCircle2, Info, X } from 'lucide-react'
import { cn } from '@/lib/cn'

type ToastKind = 'success' | 'error' | 'info'
interface ToastItem {
  id: number
  kind: ToastKind
  message: string
}

const ToastCtx = createContext<(kind: ToastKind, message: string) => void>(() => {})

const styles: Record<ToastKind, string> = {
  success: 'border-success-mark',
  error: 'border-danger-mark',
  info: 'border-primary-mark',
}

const icons: Record<ToastKind, ReactNode> = {
  success: <CheckCircle2 className="size-4 shrink-0 text-success-fg" />,
  error: <AlertTriangle className="size-4 shrink-0 text-danger-fg" />,
  info: <Info className="size-4 shrink-0 text-primary-fg" />,
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([])
  const push = useCallback((kind: ToastKind, message: string) => {
    const id = Date.now() + Math.random()
    setItems((s) => [...s, { id, kind, message }])
    setTimeout(() => setItems((s) => s.filter((t) => t.id !== id)), 4500)
  }, [])
  return (
    <ToastCtx.Provider value={push}>
      {children}
      <div
        className="pointer-events-none fixed right-4 bottom-4 z-[60] flex w-[min(20rem,calc(100vw-2rem))] flex-col gap-2"
        aria-live="polite"
      >
        {items.map((t) => (
          <div
            key={t.id}
            className={cn(
              'pointer-events-auto flex items-start gap-2 rounded-md border-l-4 bg-surface p-3 text-sm shadow-lg',
              styles[t.kind],
            )}
          >
            {icons[t.kind]}
            <p className="flex-1">{t.message}</p>
            <button
              onClick={() => setItems((s) => s.filter((x) => x.id !== t.id))}
              aria-label="Cerrar"
              className="text-fg-muted"
            >
              <X className="size-3.5" />
            </button>
          </div>
        ))}
      </div>
    </ToastCtx.Provider>
  )
}

export function useToast() {
  const push = useContext(ToastCtx)
  return useMemo(
    () => ({
      success: (m: string) => push('success', m),
      error: (m: string) => push('error', m),
      info: (m: string) => push('info', m),
    }),
    [push],
  )
}
