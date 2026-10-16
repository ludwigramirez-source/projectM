import { useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, CheckCircle2, Flag, FolderKanban, ListTodo, Table2, UserCheck, BarChart3 } from 'lucide-react'
import { api } from '@/api/client'
import { useProjects } from '@/api/projects'
import { useAuth } from '@/app/auth'
import { Avatar, Card, EmptyState, PageHeader, PageLoader, Select } from '@/components/ui'
import { Kpi } from '@/features/projects/OverviewTab'
import { cn } from '@/lib/cn'
import { fmtShortDate } from '@/lib/format'
import {
  TASK_STATUSES,
  projectStatusMeta,
  taskStatusMeta,
  toneDot,
  type ProjectStatus,
  type TaskStatus,
  type Tone,
} from '@/lib/status'

interface Workload {
  userId?: string | null
  name: string
  pendiente: number
  enProgreso: number
  enRevision: number
  bloqueada: number
  overdue: number
  total: number
}
interface Dashboard {
  activeProjects: number
  openTasks: number
  overdueTasks: number
  completedLast7Days: number
  myOpenTasks: number
  tasksByStatus: Record<TaskStatus, number>
  projectsByStatus: Record<ProjectStatus, number>
  workload: Workload[]
  overdue: { id: string; projectId: string; projectName: string; title: string; dueDate: string; assigneeName?: string | null; daysLate: number }[]
  upcomingMilestones: { id: string; projectId: string; projectName: string; name: string; dueDate: string }[]
}

/** Segmentos de carga (estados abiertos). Completada no es carga. */
const LOAD: { key: keyof Workload; status: TaskStatus }[] = [
  { key: 'pendiente', status: 'Pendiente' },
  { key: 'enProgreso', status: 'EnProgreso' },
  { key: 'enRevision', status: 'EnRevision' },
  { key: 'bloqueada', status: 'Bloqueada' },
]

export function DashboardPage() {
  const { user } = useAuth()
  const [projectId, setProjectId] = useState('')
  const { data: projects } = useProjects({ page: 1 })
  const { data, isLoading } = useQuery({
    queryKey: ['dashboard', projectId],
    queryFn: ({ signal }) => api<Dashboard>('/dashboard', { query: { projectId }, signal }),
  })

  return (
    <div>
      <PageHeader
        title={`Hola, ${user?.fullName.split(' ')[0] ?? ''}`}
        subtitle="Resumen de tus proyectos y tareas"
        actions={
          <Select aria-label="Filtrar por proyecto" value={projectId} onChange={(e) => setProjectId(e.target.value)} wrapperClassName="w-56">
            <option value="">Todos mis proyectos</option>
            {projects?.items.map((p) => <option key={p.id} value={p.id}>{p.code} · {p.name}</option>)}
          </Select>
        }
      />
      {isLoading || !data ? (
        <PageLoader />
      ) : (
        <div className="space-y-4 p-4 sm:p-6">
          <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
            <Kpi icon={<FolderKanban className="size-4" />} label="Proyectos activos" value={data.activeProjects} tone="primary" />
            <Kpi icon={<ListTodo className="size-4" />} label="Tareas abiertas" value={data.openTasks} />
            <Kpi icon={<AlertTriangle className="size-4" />} label="Vencidas" value={data.overdueTasks} tone={data.overdueTasks ? 'danger' : 'neutral'} />
            <Kpi icon={<CheckCircle2 className="size-4" />} label="Completadas (7 días)" value={data.completedLast7Days} tone="success" />
            <Kpi icon={<UserCheck className="size-4" />} label="Mis tareas abiertas" value={<Link to="/my-tasks" className="hover:text-primary-fg">{data.myOpenTasks}</Link>} />
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card title="Tareas por estado">
              <BarList
                rows={TASK_STATUSES.map((s) => ({ label: taskStatusMeta[s].label, value: data.tasksByStatus[s] ?? 0, tone: taskStatusMeta[s].tone }))}
                unit="tareas"
              />
            </Card>
            <Card title="Proyectos por estado">
              <BarList
                rows={(Object.keys(projectStatusMeta) as ProjectStatus[]).map((s) => ({
                  label: projectStatusMeta[s].label,
                  value: data.projectsByStatus[s] ?? 0,
                  tone: projectStatusMeta[s].tone,
                }))}
                unit="proyectos"
              />
            </Card>
          </div>

          <WorkloadCard rows={data.workload} />

          <div className="grid gap-4 lg:grid-cols-2">
            <Card title={<span className="flex items-center gap-2"><AlertTriangle className="size-4 text-danger-fg" /> Tareas vencidas</span>} bodyClassName="p-0">
              {data.overdue.length === 0 ? (
                <EmptyState icon={<CheckCircle2 className="size-7 text-success-fg" />} title="Nada vencido" />
              ) : (
                <ul className="divide-y divide-border">
                  {data.overdue.map((t) => (
                    <li key={t.id}>
                      <Link to={`/projects/${t.projectId}/tareas?task=${t.id}`} className="flex items-center gap-3 px-4 py-2.5 hover:bg-surface-muted">
                        <Avatar name={t.assigneeName} size="sm" />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate text-sm font-medium">{t.title}</span>
                          <span className="block truncate text-xs text-fg-muted">{t.projectName}</span>
                        </span>
                        <span className="text-right text-xs">
                          <span className="block font-medium text-danger-fg">{t.daysLate} {t.daysLate === 1 ? 'día' : 'días'} tarde</span>
                          <span className="text-fg-muted">{fmtShortDate(t.dueDate)}</span>
                        </span>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
            <Card title={<span className="flex items-center gap-2"><Flag className="size-4" /> Hitos próximos (14 días)</span>} bodyClassName="p-0">
              {data.upcomingMilestones.length === 0 ? (
                <EmptyState title="Sin hitos próximos" />
              ) : (
                <ul className="divide-y divide-border">
                  {data.upcomingMilestones.map((m) => (
                    <li key={m.id}>
                      <Link to={`/projects/${m.projectId}/seguimiento`} className="flex items-center justify-between gap-3 px-4 py-2.5 hover:bg-surface-muted">
                        <span className="min-w-0">
                          <span className="block truncate text-sm font-medium">{m.name}</span>
                          <span className="block truncate text-xs text-fg-muted">{m.projectName}</span>
                        </span>
                        <span className="text-xs whitespace-nowrap text-fg-muted">{fmtShortDate(m.dueDate)}</span>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          </div>
        </div>
      )}
    </div>
  )
}

/** Tooltip flotante sencillo para marcas de gráficos. */
function useTip() {
  const [tip, setTip] = useState<{ x: number; y: number; content: ReactNode } | null>(null)
  const bind = (content: ReactNode) => ({
    onMouseMove: (e: React.MouseEvent) => setTip({ x: e.clientX, y: e.clientY, content }),
    onMouseLeave: () => setTip(null),
  })
  const node = tip && (
    <div role="tooltip" className="pointer-events-none fixed z-50 rounded-md border border-border bg-surface px-2.5 py-1.5 text-xs shadow-lg" style={{ left: tip.x + 12, top: tip.y + 12 }}>
      {tip.content}
    </div>
  )
  return { bind, node }
}

/** Barras horizontales etiquetadas: la etiqueta y el valor siempre visibles (el color no es la única codificación). */
function BarList({ rows, unit }: { rows: { label: string; value: number; tone: Tone }[]; unit: string }) {
  const max = Math.max(1, ...rows.map((r) => r.value))
  const total = rows.reduce((a, r) => a + r.value, 0)
  const { bind, node } = useTip()
  if (total === 0) return <p className="text-sm text-fg-muted">Sin datos.</p>
  return (
    <>
      <ul className="space-y-2.5" aria-label={`Distribución de ${unit}`}>
        {rows.map((r) => (
          <li key={r.label} className="grid grid-cols-[7rem_1fr_2.5rem] items-center gap-3 text-sm" {...bind(<><strong>{r.label}</strong>: {r.value} {unit} ({Math.round((r.value / total) * 100)}%)</>)}>
            <span className="truncate text-fg-muted">{r.label}</span>
            <span className="h-3 rounded-r bg-neutral-subtle">
              {r.value > 0 && <span className={cn('block h-full rounded-r', toneDot[r.tone])} style={{ width: `${(r.value / max) * 100}%`, minWidth: 4 }} />}
            </span>
            <span className="text-right font-medium tabular-nums">{r.value}</span>
          </li>
        ))}
      </ul>
      {node}
    </>
  )
}

function WorkloadCard({ rows }: { rows: Workload[] }) {
  const [asTable, setAsTable] = useState(false)
  const { bind, node } = useTip()
  const max = Math.max(1, ...rows.map((r) => r.total))
  return (
    <Card
      title="Carga por persona (tareas abiertas)"
      actions={
        <button onClick={() => setAsTable(!asTable)} className="inline-flex items-center gap-1.5 text-xs text-fg-muted hover:text-fg" aria-pressed={asTable}>
          {asTable ? <BarChart3 className="size-3.5" /> : <Table2 className="size-3.5" />} {asTable ? 'Ver gráfico' : 'Ver tabla'}
        </button>
      }
    >
      {rows.length === 0 ? (
        <p className="text-sm text-fg-muted">No hay tareas abiertas.</p>
      ) : asTable ? (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-xs text-fg-muted">
              <tr className="border-b border-border">
                <th className="py-1.5 pr-3 font-medium">Persona</th>
                {LOAD.map((l) => <th key={l.key} className="px-3 py-1.5 text-right font-medium">{taskStatusMeta[l.status].label}</th>)}
                <th className="px-3 py-1.5 text-right font-medium">Vencidas</th>
                <th className="py-1.5 pl-3 text-right font-medium">Total</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.name} className="border-b border-border last:border-0">
                  <td className="py-1.5 pr-3">{r.name}</td>
                  {LOAD.map((l) => <td key={l.key} className="px-3 py-1.5 text-right tabular-nums">{r[l.key] as number}</td>)}
                  <td className={cn('px-3 py-1.5 text-right tabular-nums', r.overdue > 0 && 'font-medium text-danger-fg')}>{r.overdue}</td>
                  <td className="py-1.5 pl-3 text-right font-medium tabular-nums">{r.total}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <>
          <ul className="mb-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-fg-muted" aria-label="Leyenda">
            {LOAD.map((l) => (
              <li key={l.key} className="flex items-center gap-1.5">
                <span className={cn('size-2.5 rounded-sm', toneDot[taskStatusMeta[l.status].tone])} /> {taskStatusMeta[l.status].label}
              </li>
            ))}
          </ul>
          <ul className="space-y-2.5">
            {rows.map((r) => (
              <li key={r.name} className="grid grid-cols-[minmax(0,10rem)_1fr_auto] items-center gap-3 text-sm">
                <span className="flex min-w-0 items-center gap-2">
                  <Avatar name={r.userId ? r.name : null} size="sm" />
                  <span className="truncate">{r.name}</span>
                </span>
                <span className="flex h-3 gap-0.5" style={{ width: `${(r.total / max) * 100}%` }}>
                  {LOAD.filter((l) => (r[l.key] as number) > 0).map((l, i, arr) => (
                    <span
                      key={l.key}
                      className={cn('h-full', toneDot[taskStatusMeta[l.status].tone], i === arr.length - 1 && 'rounded-r')}
                      style={{ flexGrow: r[l.key] as number, minWidth: 3 }}
                      {...bind(<><strong>{r.name}</strong> · {taskStatusMeta[l.status].label}: {r[l.key] as number}</>)}
                    />
                  ))}
                </span>
                <span className="text-right text-xs whitespace-nowrap tabular-nums">
                  <span className="font-medium">{r.total}</span>
                  {r.overdue > 0 && <span className="ml-1.5 text-danger-fg">({r.overdue} vencidas)</span>}
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
      {node}
    </Card>
  )
}
