import { useState } from 'react'
import { addDays, endOfWeek } from 'date-fns'
import { CheckCircle2 } from 'lucide-react'
import { useTasks, type TaskItem } from '@/api/tasks'
import { Card, EmptyState, PageHeader, PageLoader, Tabs } from '@/components/ui'
import { toDateInput } from '@/lib/format'
import { TaskRow } from './ListView'
import { CalendarView } from './CalendarView'

type Tab = 'lista' | 'calendario'

/** Tareas asignadas al usuario en todos sus proyectos, agrupadas por vencimiento. */
export function MyTasksPage() {
  const [tab, setTab] = useState<Tab>('lista')
  const [showDone, setShowDone] = useState(false)
  const { data, isLoading } = useTasks({
    mine: true,
    includeSubtasks: true,
    sort: 'dueDate',
    status: showDone ? undefined : ['Pendiente', 'EnProgreso', 'EnRevision', 'Bloqueada'],
    pageSize: 100,
  })
  const [today] = useState(() => toDateInput(new Date()))
  const [weekEnd] = useState(() => toDateInput(endOfWeek(new Date(), { weekStartsOn: 1 })))
  const [tomorrow] = useState(() => toDateInput(addDays(new Date(), 1)))

  const open = (t: TaskItem) => t.status !== 'Completada' && !t.isOverdue
  const buckets: { title: string; tone?: string; filter: (t: TaskItem) => boolean }[] = [
    { title: 'Vencidas', tone: 'text-danger-fg', filter: (t) => t.isOverdue },
    { title: 'Hoy', filter: (t) => open(t) && t.dueDate === today },
    { title: 'Esta semana', filter: (t) => open(t) && !!t.dueDate && t.dueDate >= tomorrow && t.dueDate <= weekEnd },
    { title: 'Más adelante', filter: (t) => open(t) && !!t.dueDate && t.dueDate > weekEnd },
    { title: 'Sin fecha', filter: (t) => open(t) && !t.dueDate },
    { title: 'Completadas', filter: (t) => t.status === 'Completada' },
  ]

  return (
    <div>
      <PageHeader title="Mis tareas" subtitle="Lo que tienes asignado en todos tus proyectos">
        <Tabs value={tab} onChange={setTab} items={[{ value: 'lista', label: 'Lista' }, { value: 'calendario', label: 'Calendario' }]} />
      </PageHeader>
      {tab === 'calendario' ? (
        <CalendarView filters={{ mine: true }} canEdit showProject />
      ) : isLoading ? (
        <PageLoader />
      ) : (
        <div className="space-y-4 p-4 sm:p-6">
          <label className="inline-flex items-center gap-2 text-sm">
            <input type="checkbox" className="size-4 accent-primary-solid" checked={showDone} onChange={(e) => setShowDone(e.target.checked)} />
            Incluir completadas
          </label>
          {!data?.items.length ? (
            <Card>
              <EmptyState icon={<CheckCircle2 className="size-8 text-success-fg" />} title="¡Todo al día!" description="No tienes tareas pendientes asignadas." />
            </Card>
          ) : (
            buckets.map((b) => {
              const items = data.items.filter(b.filter)
              if (!items.length) return null
              return (
                <Card key={b.title} title={<span className={b.tone}>{b.title} ({items.length})</span>} bodyClassName="p-0">
                  <div className="overflow-x-auto">
                    <table className="w-full min-w-[720px] text-sm">
                      <tbody>
                        {items.map((t) => <TaskRow key={t.id} task={t} canEdit showProject />)}
                      </tbody>
                    </table>
                  </div>
                </Card>
              )
            })
          )}
        </div>
      )}
    </div>
  )
}
