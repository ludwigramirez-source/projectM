import type { ReactNode } from 'react'
import { cn } from '@/lib/cn'
import {
  healthMeta,
  priorityMeta,
  projectStatusMeta,
  taskStatusMeta,
  toneBadge,
  toneDot,
  type Priority,
  type ProjectHealth,
  type ProjectStatus,
  type TaskStatus,
  type Tone,
} from '@/lib/status'

export function Badge({
  tone = 'neutral',
  dot,
  children,
  className,
}: {
  tone?: Tone
  dot?: boolean
  children: ReactNode
  className?: string
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap',
        toneBadge[tone],
        className,
      )}
    >
      {dot && <span className={cn('size-1.5 rounded-full', toneDot[tone])} />}
      {children}
    </span>
  )
}

export function StatusBadge({ status, overdue }: { status: TaskStatus; overdue?: boolean }) {
  if (overdue && status !== 'Completada')
    return (
      <Badge tone="danger" dot>
        Vencida
      </Badge>
    )
  const m = taskStatusMeta[status]
  return (
    <Badge tone={m.tone} dot>
      {m.label}
    </Badge>
  )
}

export function PriorityBadge({ priority }: { priority: Priority }) {
  const m = priorityMeta[priority]
  return <Badge tone={m.tone}>{m.label}</Badge>
}

export function ProjectStatusBadge({ status }: { status: ProjectStatus }) {
  const m = projectStatusMeta[status]
  return (
    <Badge tone={m.tone} dot>
      {m.label}
    </Badge>
  )
}

export function HealthBadge({ health }: { health: ProjectHealth }) {
  const m = healthMeta[health]
  return <Badge tone={m.tone}>{m.label}</Badge>
}
