import type { ReactNode } from 'react'
import { AlertTriangle, CalendarClock, CheckCircle2, Flag, OctagonX, ShieldAlert, TrendingUp } from 'lucide-react'
import { useProjectSummary, type ProjectDetail } from '@/api/projects'
import { Card, PageLoader, ProgressBar } from '@/components/ui'
import { cn } from '@/lib/cn'
import { fmtDate, fmtMoney } from '@/lib/format'
import { TASK_STATUSES, taskStatusMeta, toneDot } from '@/lib/status'

export function Kpi({ icon, label, value, tone = 'neutral', hint }: { icon: ReactNode; label: string; value: ReactNode; tone?: 'neutral' | 'danger' | 'success' | 'primary'; hint?: ReactNode }) {
  const tones = {
    neutral: 'bg-secondary-subtle text-secondary-subtle-fg',
    danger: 'bg-danger-subtle text-danger-subtle-fg',
    success: 'bg-success-subtle text-success-subtle-fg',
    primary: 'bg-primary-subtle text-primary-subtle-fg',
  }
  return (
    <div className="flex items-center gap-3 rounded-lg border border-border bg-surface p-3">
      <span className={cn('flex size-9 shrink-0 items-center justify-center rounded-md', tones[tone])}>{icon}</span>
      <div className="min-w-0">
        <div className="text-xs leading-tight text-fg-muted">{label}</div>
        <div className="text-lg leading-tight font-semibold">{value}</div>
        {hint && <div className="text-xs text-fg-muted">{hint}</div>}
      </div>
    </div>
  )
}

export function OverviewTab({ project }: { project: ProjectDetail }) {
  const { data: s, isLoading } = useProjectSummary(project.id)
  if (isLoading || !s) return <PageLoader />
  const days = s.daysRemaining

  return (
    <div className="space-y-4 p-4 sm:p-6">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-6">
        <Kpi icon={<TrendingUp className="size-4" />} label="Avance" value={`${s.progress}%`} tone="primary" />
        <Kpi icon={<CheckCircle2 className="size-4" />} label="Completadas" value={`${s.completedTasks}/${s.totalTasks}`} tone="success" />
        <Kpi icon={<AlertTriangle className="size-4" />} label="Vencidas" value={s.overdueTasks} tone={s.overdueTasks ? 'danger' : 'neutral'} />
        <Kpi icon={<OctagonX className="size-4" />} label="Bloqueadas" value={s.blockedTasks} tone={s.blockedTasks ? 'danger' : 'neutral'} />
        <Kpi icon={<ShieldAlert className="size-4" />} label="Riesgos abiertos" value={s.openRisks} tone={s.highRisks ? 'danger' : 'neutral'} hint={s.highRisks ? `${s.highRisks} alto(s)` : undefined} />
        <Kpi
          icon={<CalendarClock className="size-4" />}
          label={days == null ? 'Finalizado' : days >= 0 ? 'Días restantes' : 'Días de retraso'}
          value={days == null ? '—' : Math.abs(days)}
          tone={days != null && days < 0 ? 'danger' : 'neutral'}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card title="Avance por estado" className="lg:col-span-2">
          <div className="mb-3 flex items-center gap-2">
            <ProgressBar value={s.progress} />
            <span className="w-12 text-right text-sm font-semibold">{s.progress}%</span>
          </div>
          {s.totalTasks === 0 ? (
            <p className="text-sm text-fg-muted">Aún no hay tareas en el proyecto.</p>
          ) : (
            <>
              <div className="flex h-3 overflow-hidden rounded-full" aria-hidden>
                {TASK_STATUSES.map((st) =>
                  s.tasksByStatus[st] ? (
                    <div key={st} className={toneDot[taskStatusMeta[st].tone]} style={{ width: `${(s.tasksByStatus[st] / s.totalTasks) * 100}%` }} />
                  ) : null,
                )}
              </div>
              <ul className="mt-3 grid grid-cols-2 gap-2 text-sm sm:grid-cols-5">
                {TASK_STATUSES.map((st) => (
                  <li key={st} className="flex items-center gap-2">
                    <span className={cn('size-2.5 rounded-full', toneDot[taskStatusMeta[st].tone])} />
                    <span className="text-fg-muted">{taskStatusMeta[st].label}</span>
                    <span className="ml-auto font-medium">{s.tasksByStatus[st]}</span>
                  </li>
                ))}
              </ul>
            </>
          )}
        </Card>

        <Card title="Datos del proyecto">
          <dl className="space-y-2 text-sm">
            <Row label="Gestor">{project.managerName}</Row>
            <Row label="Planeado">
              {fmtDate(project.plannedStart)} – {fmtDate(project.plannedEnd)}
            </Row>
            <Row label="Real">
              {project.actualStart ? `${fmtDate(project.actualStart)} – ${project.actualEnd ? fmtDate(project.actualEnd) : 'en curso'}` : 'Sin iniciar'}
            </Row>
            {project.budget != null && <Row label="Presupuesto">{fmtMoney(project.budget, project.currency)}</Row>}
          </dl>
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Objetivo y alcance">
          <h4 className="text-xs font-semibold text-fg-muted uppercase">Objetivo</h4>
          <p className="mt-1 text-sm whitespace-pre-wrap">{project.objective || '—'}</p>
          <h4 className="mt-4 text-xs font-semibold text-fg-muted uppercase">Alcance</h4>
          <p className="mt-1 text-sm whitespace-pre-wrap">{project.scope || '—'}</p>
        </Card>
        <Card title={<span className="flex items-center gap-2"><Flag className="size-4" /> Hitos</span>}>
          {s.milestones.length === 0 ? (
            <p className="text-sm text-fg-muted">Sin hitos definidos.</p>
          ) : (
            <ul className="space-y-3">
              {s.milestones.map((m) => {
                const pct = m.totalTasks ? Math.round((m.completedTasks / m.totalTasks) * 100) : m.completedAt ? 100 : 0
                return (
                  <li key={m.id}>
                    <div className="flex items-center justify-between gap-2 text-sm">
                      <span className={cn('font-medium', m.completedAt && 'text-success-fg')}>{m.name}</span>
                      <span className={cn('text-xs', m.isOverdue ? 'font-medium text-danger-fg' : 'text-fg-muted')}>
                        {m.completedAt ? 'Completado' : fmtDate(m.dueDate)}
                      </span>
                    </div>
                    <div className="mt-1">
                      <ProgressBar value={pct} tone={m.isOverdue ? 'danger' : 'success'} />
                    </div>
                  </li>
                )
              })}
            </ul>
          )}
        </Card>
      </div>
    </div>
  )
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-fg-muted">{label}</dt>
      <dd className="text-right font-medium">{children}</dd>
    </div>
  )
}
