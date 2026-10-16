import { useState } from 'react'
import { DndContext, KeyboardSensor, MouseSensor, TouchSensor, pointerWithin, useDraggable, useDroppable, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  addDays,
  addMonths,
  addWeeks,
  differenceInCalendarDays,
  eachDayOfInterval,
  endOfMonth,
  endOfWeek,
  format,
  isSameMonth,
  isToday,
  parseISO,
  startOfMonth,
  startOfWeek,
} from 'date-fns'
import { es } from 'date-fns/locale'
import { ChevronLeft, ChevronRight, Flag } from 'lucide-react'
import { api, errorMessage } from '@/api/client'
import { invalidateTasks, type TaskFilters, type TaskItem } from '@/api/tasks'
import { Button, PageLoader, useToast } from '@/components/ui'
import { cn } from '@/lib/cn'
import { toDateInput } from '@/lib/format'
import { taskStatusMeta, toneBadge, toneDot, type Tone } from '@/lib/status'
import { useTaskDrawer } from './TaskDrawer'

interface CalendarMilestone {
  id: string
  projectId: string
  projectName: string
  name: string
  dueDate: string
  completedAt?: string | null
}
interface CalendarData {
  tasks: TaskItem[]
  milestones: CalendarMilestone[]
}

type Mode = 'mes' | 'semana' | 'agenda'
const MODE_LABEL: Record<Mode, string> = { mes: 'Mes', semana: 'Semana', agenda: 'Agenda' }
/** En pantallas pequeñas la grilla de 7 columnas no es legible: se arranca en Agenda. */
const initialMode = (): Mode =>
  typeof matchMedia === 'function' && matchMedia('(max-width: 639px)').matches ? 'agenda' : 'mes'
const WEEK = { weekStartsOn: 1 as const, locale: es }

/** Calendario mensual/semanal. Las tareas se ubican por vencimiento (o inicio); se reprograman arrastrando. */
export function CalendarView({ projectId, filters, canEdit, showProject }: { projectId?: string; filters?: TaskFilters; canEdit: boolean; showProject?: boolean }) {
  const [mode, setMode] = useState<Mode>(initialMode)
  const [cursor, setCursor] = useState(() => new Date())
  const { open } = useTaskDrawer()
  const qc = useQueryClient()
  const toast = useToast()

  const rangeStart = mode === 'mes' ? startOfWeek(startOfMonth(cursor), WEEK) : startOfWeek(cursor, WEEK)
  const rangeEnd = mode === 'mes' ? endOfWeek(endOfMonth(cursor), WEEK) : endOfWeek(cursor, WEEK)
  const days = eachDayOfInterval({ start: rangeStart, end: rangeEnd })
  const from = toDateInput(rangeStart)
  const to = toDateInput(rangeEnd)

  const { data, isLoading } = useQuery({
    queryKey: ['calendar', projectId ?? 'all', from, to, filters?.assigneeId, filters?.mine],
    queryFn: ({ signal }) =>
      api<CalendarData>('/calendar', { query: { from, to, projectId, assigneeId: filters?.assigneeId, mine: filters?.mine }, signal }),
  })

  const reschedule = useMutation({
    mutationFn: (b: { id: string; startDate?: string | null; dueDate?: string | null }) =>
      api<TaskItem>(`/tasks/${b.id}/dates`, { method: 'PATCH', body: b }),
    onSettled: (_d, _e, b) => invalidateTasks(qc, projectId, b.id),
    onError: (e) => toast.error(errorMessage(e)),
  })

  // Táctil con retardo para no confundir arrastrar con desplazar la página.
  const sensors = useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 5 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 250, tolerance: 8 } }),
    useSensor(KeyboardSensor),
  )

  const visibleTasks = (data?.tasks ?? []).filter(
    (t) =>
      (!filters?.status?.length || filters.status.includes(t.status)) &&
      (!filters?.priority || t.priority === filters.priority) &&
      (!filters?.tagId || t.tags.some((x) => x.id === filters.tagId)) &&
      (!filters?.listId || t.listId === filters.listId) &&
      (!filters?.q || t.title.toLowerCase().includes(filters.q.toLowerCase())) &&
      (!filters?.overdue || t.isOverdue),
  )
  const dayOf = (t: TaskItem) => t.dueDate ?? t.startDate!

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over) return
    const task = visibleTasks.find((t) => t.id === active.id)
    if (!task) return
    const target = String(over.id)
    const current = dayOf(task)
    if (target === current) return
    // Conserva la duración: desplaza inicio y vencimiento en los mismos días.
    const delta = differenceInCalendarDays(parseISO(target), parseISO(current))
    const shift = (d?: string | null) => (d ? toDateInput(addDays(parseISO(d), delta)) : d)
    qc.setQueriesData<CalendarData>({ queryKey: ['calendar'] }, (old) =>
      old && { ...old, tasks: old.tasks.map((t) => (t.id === task.id ? { ...t, startDate: shift(t.startDate), dueDate: shift(t.dueDate) } : t)) },
    )
    reschedule.mutate({ id: task.id, startDate: shift(task.startDate), dueDate: shift(task.dueDate) })
  }

  const step = (n: number) => setCursor((c) => (mode === 'mes' ? addMonths(c, n) : addWeeks(c, n)))
  const title = mode === 'mes' ? format(cursor, 'LLLL yyyy', { locale: es }) : `${format(rangeStart, 'd MMM', { locale: es })} – ${format(rangeEnd, 'd MMM yyyy', { locale: es })}`

  return (
    <div className="p-4 sm:p-6">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Button variant="secondary" size="sm" onClick={() => setCursor(new Date())}>Hoy</Button>
          <Button variant="ghost" size="sm" aria-label="Anterior" icon={<ChevronLeft className="size-4" />} onClick={() => step(-1)} />
          <Button variant="ghost" size="sm" aria-label="Siguiente" icon={<ChevronRight className="size-4" />} onClick={() => step(1)} />
          <h2 className="text-base font-semibold capitalize sm:text-lg">{title}</h2>
        </div>
        <div role="tablist" className="flex rounded-md border border-border p-0.5 text-sm">
          {(['mes', 'semana', 'agenda'] as Mode[]).map((m) => (
            <button key={m} role="tab" aria-selected={mode === m} onClick={() => setMode(m)}
              className={cn('rounded px-3 py-1 touch:py-2', mode === m ? 'bg-primary-subtle font-medium text-primary-fg' : 'text-fg-muted')}>
              {MODE_LABEL[m]}
            </button>
          ))}
        </div>
      </div>

      {isLoading ? (
        <PageLoader />
      ) : mode === 'agenda' ? (
        <Agenda days={days} tasks={visibleTasks} dayOf={dayOf} milestones={data?.milestones ?? []} onOpen={open} showProject={showProject} />
      ) : (
        <DndContext sensors={canEdit ? sensors : []} collisionDetection={pointerWithin} onDragEnd={onDragEnd}>
          <div className="overflow-x-auto rounded-lg border border-border bg-surface">
            <div className="grid min-w-[720px] grid-cols-7 border-b border-border bg-surface-muted text-xs font-medium text-fg-muted">
              {days.slice(0, 7).map((d) => (
                <div key={d.toISOString()} className="px-2 py-1.5 capitalize">{format(d, 'EEE', { locale: es })}</div>
              ))}
            </div>
            <div className="grid min-w-[720px] grid-cols-7">
              {days.map((d) => {
                const key = toDateInput(d)
                return (
                  <DayCell
                    key={key}
                    date={d}
                    iso={key}
                    dim={mode === 'mes' && !isSameMonth(d, cursor)}
                    tall={mode === 'semana'}
                    tasks={visibleTasks.filter((t) => dayOf(t) === key)}
                    milestones={(data?.milestones ?? []).filter((m) => m.dueDate === key)}
                    onOpen={open}
                    canEdit={canEdit}
                    showProject={showProject}
                  />
                )
              })}
            </div>
          </div>
        </DndContext>
      )}
      <p className="mt-2 text-xs text-fg-muted">
        Las tareas aparecen el día de su vencimiento (o de inicio si no tienen vencimiento).{' '}
        {canEdit && mode !== 'agenda' && 'Arrastra una tarea (en táctil, mantén presionado) para reprogramarla conservando su duración.'}
      </p>
    </div>
  )
}

/** Lista por día (vista pensada para móvil). */
function Agenda({ days, tasks, dayOf, milestones, onOpen, showProject }: {
  days: Date[]
  tasks: TaskItem[]
  dayOf: (t: TaskItem) => string
  milestones: CalendarMilestone[]
  onOpen: (id: string) => void
  showProject?: boolean
}) {
  return (
    <ol className="space-y-3">
      {days.map((d) => {
        const key = toDateInput(d)
        const dayTasks = tasks.filter((t) => dayOf(t) === key)
        const dayMilestones = milestones.filter((m) => m.dueDate === key)
        return (
          <li key={key} className="rounded-lg border border-border bg-surface">
            <h3 className={cn('flex items-center gap-2 border-b border-border px-3 py-2 text-sm font-semibold', isToday(d) && 'text-primary-fg')}>
              <span className="first-letter:uppercase">{format(d, "EEEE d 'de' MMMM", { locale: es })}</span>
              {isToday(d) && <span className="rounded-full bg-primary-subtle px-2 text-xs font-medium text-primary-subtle-fg">Hoy</span>}
            </h3>
            {dayTasks.length === 0 && dayMilestones.length === 0 ? (
              <p className="px-3 py-2 text-xs text-fg-muted">Sin tareas</p>
            ) : (
              <ul className="divide-y divide-border">
                {dayMilestones.map((m) => (
                  <li key={m.id} className={cn('flex items-center gap-2 px-3 py-2 text-sm font-medium', m.completedAt ? toneBadge.success : toneBadge.accent)}>
                    <Flag className="size-4 shrink-0" /> Hito: {m.name}
                  </li>
                ))}
                {dayTasks.map((t) => (
                  <li key={t.id}>
                    <button onClick={() => onOpen(t.id)} className="flex w-full items-center gap-3 px-3 py-2.5 text-left hover:bg-surface-muted">
                      <span className={cn('size-2.5 shrink-0 rounded-full', toneDot[t.isOverdue ? 'danger' : taskStatusMeta[t.status].tone])} />
                      <span className="min-w-0 flex-1">
                        <span className={cn('block text-sm', t.status === 'Completada' && 'text-fg-muted line-through')}>{t.title}</span>
                        <span className="block truncate text-xs text-fg-muted">
                          {t.isOverdue ? 'Vencida' : taskStatusMeta[t.status].label}
                          {showProject ? ` · ${t.projectName}` : ` · ${t.listName}`}
                          {t.assigneeName && ` · ${t.assigneeName}`}
                        </span>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </li>
        )
      })}
    </ol>
  )
}

function DayCell({ date, iso, dim, tall, tasks, milestones, onOpen, canEdit, showProject }: {
  date: Date
  iso: string
  dim: boolean
  tall: boolean
  tasks: TaskItem[]
  milestones: CalendarMilestone[]
  onOpen: (id: string) => void
  canEdit: boolean
  showProject?: boolean
}) {
  const { setNodeRef, isOver } = useDroppable({ id: iso })
  const max = tall ? 50 : 4
  return (
    <div ref={setNodeRef} className={cn('border-r border-b border-border p-1 [&:nth-child(7n)]:border-r-0', tall ? 'min-h-96' : 'min-h-28', dim && 'bg-surface-muted/60', isOver && 'bg-primary-subtle')}>
      <div className={cn('mb-1 flex size-6 items-center justify-center rounded-full text-xs', isToday(date) ? 'bg-primary-solid font-semibold text-on-primary-solid' : dim ? 'text-fg-muted' : '')}>
        {format(date, 'd')}
      </div>
      <div className="space-y-1">
        {milestones.map((m) => (
          <div key={m.id} title={`Hito: ${m.name}${showProject ? ` (${m.projectName})` : ''}`}
            className={cn('flex items-center gap-1 truncate rounded px-1.5 py-0.5 text-[11px] font-medium', m.completedAt ? toneBadge.success : toneBadge.accent)}>
            <Flag className="size-3 shrink-0" /> {m.name}
          </div>
        ))}
        {tasks.slice(0, max).map((t) => <TaskChip key={t.id} task={t} onOpen={onOpen} canEdit={canEdit} showProject={showProject} />)}
        {tasks.length > max && <div className="px-1 text-[11px] text-fg-muted">+{tasks.length - max} más</div>}
      </div>
    </div>
  )
}

function TaskChip({ task, onOpen, canEdit, showProject }: { task: TaskItem; onOpen: (id: string) => void; canEdit: boolean; showProject?: boolean }) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: task.id, disabled: !canEdit })
  const tone: Tone = task.isOverdue ? 'danger' : taskStatusMeta[task.status].tone
  return (
    <button
      ref={setNodeRef}
      style={transform ? { transform: `translate(${transform.x}px, ${transform.y}px)` } : undefined}
      {...attributes}
      {...listeners}
      onClick={() => onOpen(task.id)}
      title={`${task.title} · ${taskStatusMeta[task.status].label}${showProject ? ` · ${task.projectName}` : ''}`}
      className={cn('block w-full truncate rounded px-1.5 py-0.5 text-left text-[11px]', toneBadge[tone], task.status === 'Completada' && 'line-through', isDragging && 'relative z-10 shadow-lg')}
    >
      {task.title}
    </button>
  )
}
