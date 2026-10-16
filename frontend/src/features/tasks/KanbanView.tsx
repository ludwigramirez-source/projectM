import { useMemo, useState } from 'react'
import {
  DndContext,
  DragOverlay,
  KeyboardSensor,
  PointerSensor,
  closestCorners,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { SortableContext, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { CalendarDays, MessageSquare, Paperclip, Plus } from 'lucide-react'
import type { ProjectDetail } from '@/api/projects'
import { useMoveTask, useTaskMutations, type TaskItem } from '@/api/tasks'
import { errorMessage } from '@/api/client'
import { Avatar, Badge, PriorityBadge, useToast } from '@/components/ui'
import { cn } from '@/lib/cn'
import { fmtShortDate } from '@/lib/format'
import { TASK_STATUSES, taskStatusMeta, toneBorder, toneDot, type TaskStatus, type Tone } from '@/lib/status'
import { BlockReasonModal, useTaskDrawer } from './TaskDrawer'

type Columns = Record<TaskStatus, string[]>

const group = (tasks: TaskItem[]): Columns => {
  const cols = Object.fromEntries(TASK_STATUSES.map((s) => [s, [] as string[]])) as Columns
  for (const t of [...tasks].sort((a, b) => a.position - b.position)) cols[t.status].push(t.id)
  return cols
}

/** Vista Kanban por estado con arrastrar y soltar (mouse, táctil y teclado). */
export function KanbanView({ project, tasks, listId }: { project: ProjectDetail; tasks: TaskItem[]; listId?: string }) {
  const can = project.permissions.canEditTasks
  const byId = useMemo(() => new Map(tasks.map((t) => [t.id, t])), [tasks])
  const serverColumns = useMemo(() => group(tasks), [tasks])
  // Columnas locales solo mientras se arrastra; fuera de eso se derivan de los datos del servidor.
  const [dragColumns, setDragColumns] = useState<Columns | null>(null)
  const columns = dragColumns ?? serverColumns
  const [activeId, setActiveId] = useState<string | null>(null)
  const [pendingBlock, setPendingBlock] = useState<{ id: string; beforeId: string | null; afterId: string | null } | null>(null)
  const move = useMoveTask(project.id)
  const toast = useToast()

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  const findColumn = (id: string): TaskStatus | undefined =>
    (TASK_STATUSES as string[]).includes(id) ? (id as TaskStatus) : TASK_STATUSES.find((s) => columns[s].includes(id))

  const onDragStart = (e: DragStartEvent) => {
    setActiveId(String(e.active.id))
    setDragColumns(serverColumns)
  }

  const onDragOver = ({ active, over }: DragOverEvent) => {
    if (!over) return
    const from = findColumn(String(active.id))
    const to = findColumn(String(over.id))
    if (!from || !to || from === to) return
    setDragColumns((prev) => {
      const cols = prev ?? serverColumns
      const target = cols[to].filter((x) => x !== active.id)
      const overIndex = target.indexOf(String(over.id))
      target.splice(overIndex < 0 ? target.length : overIndex, 0, String(active.id))
      return { ...cols, [from]: cols[from].filter((x) => x !== active.id), [to]: target }
    })
  }

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    const id = String(active.id)
    setActiveId(null)
    const cols = dragColumns ?? serverColumns
    setDragColumns(null)
    if (!over) return
    const to = findColumn(String(over.id))
    if (!to) return

    const ids = [...cols[to]]
    const from = ids.indexOf(id)
    const overIndex = ids.indexOf(String(over.id))
    if (from >= 0 && overIndex >= 0 && from !== overIndex) {
      ids.splice(from, 1)
      ids.splice(overIndex, 0, id)
    }
    const index = ids.indexOf(id)
    const beforeId = ids[index - 1] ?? null
    const afterId = ids[index + 1] ?? null
    const task = byId.get(id)
    if (!task) return
    const unchanged = task.status === to && beforeId === (serverColumns[to][serverColumns[to].indexOf(id) - 1] ?? null)
    if (unchanged) return

    if (to === 'Bloqueada' && task.status !== 'Bloqueada') return setPendingBlock({ id, beforeId, afterId })
    move.mutate({ id, status: to, beforeId, afterId }, { onError: (e) => toast.error(errorMessage(e)) })
  }

  const active = activeId ? byId.get(activeId) : undefined

  return (
    <div className="overflow-x-auto p-4 sm:p-6">
      <DndContext
        sensors={can ? sensors : []}
        collisionDetection={closestCorners}
        onDragStart={onDragStart}
        onDragOver={onDragOver}
        onDragEnd={onDragEnd}
        onDragCancel={() => {
          setActiveId(null)
          setDragColumns(null)
        }}
        accessibility={{
          screenReaderInstructions: { draggable: 'Para mover una tarjeta, presiona espacio, usa las flechas y presiona espacio otra vez para soltar.' },
        }}
      >
        <div className="flex min-w-max gap-3">
          {TASK_STATUSES.map((status) => (
            <Column key={status} project={project} status={status} ids={columns[status]} byId={byId} can={can} listId={listId} />
          ))}
        </div>
        <DragOverlay>{active && <Card task={active} dragging />}</DragOverlay>
      </DndContext>
      {pendingBlock && (
        <BlockReasonModal
          onCancel={() => setPendingBlock(null)}
          loading={move.isPending}
          onConfirm={(blockedReason) =>
            move.mutate(
              { ...pendingBlock, status: 'Bloqueada', blockedReason },
              { onSuccess: () => setPendingBlock(null), onError: (e) => toast.error(errorMessage(e)) },
            )
          }
        />
      )}
    </div>
  )
}

function Column({ project, status, ids, byId, can, listId }: { project: ProjectDetail; status: TaskStatus; ids: string[]; byId: Map<string, TaskItem>; can: boolean; listId?: string }) {
  const { setNodeRef, isOver } = useDroppable({ id: status })
  const meta = taskStatusMeta[status]
  const [adding, setAdding] = useState(false)
  const [title, setTitle] = useState('')
  const m = useTaskMutations(project.id)
  const toast = useToast()
  const targetList = listId ?? project.lists[0]?.id
  return (
    <section className={cn('flex w-72 shrink-0 flex-col rounded-lg border-t-4 bg-surface-muted', toneBorder[meta.tone])} aria-label={`Columna ${meta.label}`}>
      <header className="flex items-center justify-between px-3 py-2">
        <h3 className="flex items-center gap-2 text-sm font-semibold">
          <span className={cn('size-2 rounded-full', toneDot[meta.tone])} />
          {meta.label}
          <span className="rounded-full bg-surface px-1.5 text-xs font-normal text-fg-muted">{ids.length}</span>
        </h3>
        {can && status !== 'Bloqueada' && targetList && (
          <button onClick={() => setAdding(true)} className="rounded p-1 text-fg-muted hover:bg-surface hover:text-fg" aria-label={`Nueva tarea en ${meta.label}`}>
            <Plus className="size-4" />
          </button>
        )}
      </header>
      <div ref={setNodeRef} className={cn('flex min-h-24 flex-1 flex-col gap-2 px-2 pb-2 transition-colors', isOver && 'bg-primary-subtle')}>
        {adding && (
          <input
            autoFocus
            aria-label="Título de la nueva tarea"
            placeholder="Título y Enter"
            className="rounded-md border border-primary-mark bg-surface px-2 py-2 text-sm focus:outline-none"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            onBlur={() => !title && setAdding(false)}
            onKeyDown={(e) => {
              if (e.key === 'Escape') setAdding(false)
              if (e.key !== 'Enter' || !title.trim() || !targetList) return
              m.create.mutate(
                { listId: targetList, title: title.trim(), status },
                { onSuccess: () => { setTitle(''); setAdding(false) }, onError: (err) => toast.error(errorMessage(err)) },
              )
            }}
          />
        )}
        <SortableContext items={ids} strategy={verticalListSortingStrategy}>
          {ids.map((id) => {
            const t = byId.get(id)
            return t ? <SortableCard key={id} task={t} disabled={!can} /> : null
          })}
        </SortableContext>
      </div>
    </section>
  )
}

function SortableCard({ task, disabled }: { task: TaskItem; disabled: boolean }) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: task.id, disabled })
  return (
    <div
      ref={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      className={cn(isDragging && 'opacity-40')}
      {...attributes}
      {...listeners}
    >
      <Card task={task} />
    </div>
  )
}

function Card({ task, dragging }: { task: TaskItem; dragging?: boolean }) {
  const { open } = useTaskDrawer()
  return (
    <article
      onClick={() => open(task.id)}
      className={cn(
        'cursor-pointer rounded-md border border-border bg-surface p-3 text-sm shadow-sm hover:border-border-strong',
        dragging && 'rotate-2 shadow-lg',
        task.isOverdue && 'border-l-4 border-l-danger-mark',
      )}
    >
      {task.tags.length > 0 && (
        <div className="mb-1.5 flex flex-wrap gap-1">
          {task.tags.map((tg) => <Badge key={tg.id} tone={tg.color as Tone} className="py-0">{tg.name}</Badge>)}
        </div>
      )}
      <p className={cn('font-medium', task.status === 'Completada' && 'text-fg-muted line-through')}>{task.title}</p>
      <p className="mt-0.5 text-xs text-fg-muted">{task.listName}</p>
      <div className="mt-2 flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-xs text-fg-muted">
          {task.dueDate && (
            <span className={cn('inline-flex items-center gap-1', task.isOverdue && 'font-medium text-danger-fg')}>
              <CalendarDays className="size-3.5" />
              {fmtShortDate(task.dueDate)}
            </span>
          )}
          {task.subtaskCount > 0 && <span>☑ {task.completedSubtasks}/{task.subtaskCount}</span>}
          {task.attachmentCount > 0 && <span className="inline-flex items-center gap-0.5"><Paperclip className="size-3" />{task.attachmentCount}</span>}
          {task.commentCount > 0 && <span className="inline-flex items-center gap-0.5"><MessageSquare className="size-3" />{task.commentCount}</span>}
        </div>
        <div className="flex items-center gap-1.5">
          {task.priority !== 'Normal' && <PriorityBadge priority={task.priority} />}
          <Avatar name={task.assigneeName} size="sm" />
        </div>
      </div>
    </article>
  )
}
