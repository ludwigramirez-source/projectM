import type { ReactNode } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from './Button'
import { initials } from '@/lib/format'

export function Card({
  children,
  className,
  title,
  actions,
  bodyClassName,
}: {
  children: ReactNode
  className?: string
  title?: ReactNode
  actions?: ReactNode
  bodyClassName?: string
}) {
  return (
    <section className={cn('rounded-lg border border-border bg-surface', className)}>
      {(title || actions) && (
        <header className="flex items-center justify-between gap-2 border-b border-border px-4 py-3">
          <h3 className="text-sm font-semibold">{title}</h3>
          {actions}
        </header>
      )}
      <div className={cn('p-4', bodyClassName)}>{children}</div>
    </section>
  )
}

export function EmptyState({
  icon,
  title,
  description,
  action,
}: {
  icon?: ReactNode
  title: string
  description?: string
  action?: ReactNode
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 px-4 py-12 text-center">
      {icon && <div className="text-secondary-400">{icon}</div>}
      <p className="font-medium">{title}</p>
      {description && <p className="max-w-sm text-sm text-fg-muted">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  )
}

const AVATAR_TONES = [
  'bg-primary-700 text-white',
  'bg-secondary-700 text-white',
  'bg-success-700 text-white',
  'bg-accent-800 text-white',
  'bg-primary-200 text-primary-900',
  'bg-accent-300 text-neutral-900',
]

export function Avatar({
  name,
  size = 'md',
  className,
}: {
  name?: string | null
  size?: 'sm' | 'md'
  className?: string
}) {
  if (!name)
    return (
      <span
        className={cn(
          'inline-flex shrink-0 rounded-full border border-dashed border-border-strong',
          size === 'sm' ? 'size-6' : 'size-8',
          className,
        )}
        title="Sin asignar"
      />
    )
  const hash = [...name].reduce((a, c) => a + c.charCodeAt(0), 0)
  return (
    <span
      title={name}
      className={cn(
        'inline-flex shrink-0 items-center justify-center rounded-full font-semibold',
        size === 'sm' ? 'size-6 text-[10px]' : 'size-8 text-xs',
        AVATAR_TONES[hash % AVATAR_TONES.length],
        className,
      )}
    >
      {initials(name)}
    </span>
  )
}

export function Pagination({
  page,
  pageSize,
  total,
  onChange,
}: {
  page: number
  pageSize: number
  total: number
  onChange: (p: number) => void
}) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  if (total <= pageSize) return null
  return (
    <nav className="flex items-center justify-between gap-2 px-1 py-2 text-sm text-fg-muted" aria-label="Paginación">
      <span>
        {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, total)} de {total}
      </span>
      <div className="flex items-center gap-1">
        <Button
          variant="ghost"
          size="sm"
          disabled={page <= 1}
          onClick={() => onChange(page - 1)}
          aria-label="Anterior"
          icon={<ChevronLeft className="size-4" />}
        />
        <span className="px-2">
          {page} / {pages}
        </span>
        <Button
          variant="ghost"
          size="sm"
          disabled={page >= pages}
          onClick={() => onChange(page + 1)}
          aria-label="Siguiente"
          icon={<ChevronRight className="size-4" />}
        />
      </div>
    </nav>
  )
}

export function Tabs<T extends string>({
  value,
  onChange,
  items,
}: {
  value: T
  onChange: (v: T) => void
  items: { value: T; label: ReactNode }[]
}) {
  return (
    <div role="tablist" className="flex gap-1 overflow-x-auto border-b border-border">
      {items.map((it) => (
        <button
          key={it.value}
          role="tab"
          aria-selected={value === it.value}
          onClick={() => onChange(it.value)}
          className={cn(
            '-mb-px inline-flex items-center gap-1.5 border-b-2 px-3 py-2 text-sm font-medium whitespace-nowrap transition-colors',
            value === it.value
              ? 'border-primary-600 text-primary-800'
              : 'border-transparent text-fg-muted hover:text-fg',
          )}
        >
          {it.label}
        </button>
      ))}
    </div>
  )
}

export function ProgressBar({ value, tone = 'success' }: { value: number; tone?: 'success' | 'primary' | 'danger' }) {
  const color = { success: 'bg-success-500', primary: 'bg-primary-500', danger: 'bg-danger-500' }[tone]
  const v = Math.max(0, Math.min(100, value))
  return (
    <div
      className="h-2 w-full overflow-hidden rounded-full bg-neutral-200"
      role="progressbar"
      aria-valuenow={v}
      aria-valuemin={0}
      aria-valuemax={100}
    >
      <div className={cn('h-full rounded-full transition-all', color)} style={{ width: `${v}%` }} />
    </div>
  )
}

export function PageHeader({
  title,
  subtitle,
  actions,
  children,
}: {
  title: ReactNode
  subtitle?: ReactNode
  actions?: ReactNode
  children?: ReactNode
}) {
  return (
    <div className="border-b border-border bg-surface px-4 pt-4 sm:px-6">
      <div className="flex flex-wrap items-start justify-between gap-3 pb-3">
        <div className="min-w-0">
          <h1 className="truncate text-xl font-semibold">{title}</h1>
          {subtitle && <div className="mt-1 text-sm text-fg-muted">{subtitle}</div>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
      {children}
    </div>
  )
}
