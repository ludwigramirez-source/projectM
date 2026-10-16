import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { AtSign, Bell, CalendarClock, CheckCheck, FolderKanban, MessageSquare, UserCheck } from 'lucide-react'
import { useNotificationMutations, useNotifications, useUnreadCount, type AppNotification } from '@/api/collab'
import { Spinner } from '@/components/ui'
import { cn } from '@/lib/cn'
import { fmtRelative } from '@/lib/format'

const icons: Record<string, typeof Bell> = {
  Mention: AtSign,
  Comment: MessageSquare,
  TaskAssigned: UserCheck,
  TaskDueSoon: CalendarClock,
  TaskOverdue: CalendarClock,
  AddedToProject: FolderKanban,
  ProjectClosed: FolderKanban,
  ProjectReopened: FolderKanban,
}

export function NotificationBell() {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const { data: unread } = useUnreadCount()
  const { data, isLoading } = useNotifications(open)
  const { read, readAll } = useNotificationMutations()
  const navigate = useNavigate()
  const count = unread?.count ?? 0

  useEffect(() => {
    const h = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && setOpen(false)
    document.addEventListener('mousedown', h)
    return () => document.removeEventListener('mousedown', h)
  }, [])

  const go = (n: AppNotification) => {
    if (!n.readAt) read.mutate(n.id)
    setOpen(false)
    if (n.link) navigate(n.link)
  }

  return (
    <div className="relative" ref={ref}>
      <button
        onClick={() => setOpen((o) => !o)}
        className="relative rounded-md p-2 text-fg-muted hover:bg-surface-muted hover:text-fg"
        aria-label={count ? `Notificaciones (${count} sin leer)` : 'Notificaciones'}
        aria-expanded={open}
      >
        <Bell className="size-5" />
        {count > 0 && (
          <span className="absolute top-1 right-1 flex min-w-4 items-center justify-center rounded-full bg-danger-mark px-1 text-[10px] font-bold text-white">
            {count > 99 ? '99+' : count}
          </span>
        )}
      </button>
      {open && (
        <div className="absolute right-0 z-30 mt-1 w-[min(24rem,calc(100vw-1.5rem))] overflow-hidden rounded-lg border border-border bg-surface shadow-xl">
          <header className="flex items-center justify-between border-b border-border px-4 py-2.5">
            <h2 className="text-sm font-semibold">Notificaciones</h2>
            {count > 0 && (
              <button onClick={() => readAll.mutate()} className="inline-flex items-center gap-1 text-xs text-primary-fg hover:underline">
                <CheckCheck className="size-3.5" /> Marcar todas como leídas
              </button>
            )}
          </header>
          <div className="max-h-[70vh] overflow-y-auto">
            {isLoading ? (
              <div className="flex justify-center p-6 text-primary-fg"><Spinner /></div>
            ) : !data?.items.length ? (
              <p className="p-6 text-center text-sm text-fg-muted">No tienes notificaciones.</p>
            ) : (
              <ul className="divide-y divide-border">
                {data.items.map((n) => {
                  const Icon = icons[n.type] ?? Bell
                  return (
                    <li key={n.id}>
                      <button onClick={() => go(n)} className={cn('flex w-full gap-3 px-4 py-3 text-left hover:bg-surface-muted', !n.readAt && 'bg-primary-subtle')}>
                        <span className={cn('mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full', n.type === 'TaskOverdue' ? 'bg-danger-subtle text-danger-subtle-fg' : 'bg-primary-subtle text-primary-subtle-fg')}>
                          <Icon className="size-3.5" />
                        </span>
                        <span className="min-w-0 flex-1">
                          <span className={cn('block text-sm', !n.readAt && 'font-medium')}>{n.title}</span>
                          {n.body && <span className="block truncate text-xs text-fg-muted">{n.body}</span>}
                          <span className="block text-xs text-fg-muted">{fmtRelative(n.createdAt)}</span>
                        </span>
                        {!n.readAt && <span className="mt-2 size-2 shrink-0 rounded-full bg-primary-mark" aria-label="Sin leer" />}
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
