import { useState } from 'react'
import { ChevronRight, MessageSquare, Paperclip, Plus, Settings2 } from 'lucide-react'
import type { ProjectDetail } from '@/api/projects'
import { useTaskMutations, type TaskItem } from '@/api/tasks'
import { errorMessage } from '@/api/client'
import { Avatar, Badge, EmptyState, PriorityBadge, StatusBadge, useToast } from '@/components/ui'
import { cn } from '@/lib/cn'
import { fmtShortDate } from '@/lib/format'
import type { Tone } from '@/lib/status'
import { useTaskDrawer } from './TaskDrawer'
import { ListManager } from './ListManager'

/** Vista Lista: tareas agrupadas por lista, con alta rápida por grupo. */
export function ListView({ project, tasks, grouped = true }: { project: ProjectDetail; tasks: TaskItem[]; grouped?: boolean }) {
  const [managing, setManaging] = useState(false)
  const groups = grouped
    ? project.lists.map((l) => ({ id: l.id, name: l.name, tasks: tasks.filter((t) => t.listId === l.id) }))
    : [{ id: 'all', name: 'Tareas', tasks }]
  const visibleGroups = groups.filter((g) => g.tasks.length > 0 || grouped)

  return (
    <div className="space-y-4 p-4 sm:p-6">
      {project.permissions.canManage && (
        <div className="flex justify-end">
          <button onClick={() => setManaging(true)} className="inline-flex items-center gap-1.5 text-xs text-fg-muted hover:text-fg">
            <Settings2 className="size-3.5" /> Listas y campos
          </button>
        </div>
      )}
      {visibleGroups.map((g) => (
        <TaskGroup key={g.id} project={project} listId={g.id === 'all' ? project.lists[0]?.id : g.id} name={g.name} tasks={g.tasks} />
      ))}
      {visibleGroups.length === 0 && <EmptyState title="No hay tareas que coincidan" />}
      {managing && <ListManager project={project} onClose={() => setManaging(false)} />}
    </div>
  )
}

function TaskGroup({ project, listId, name, tasks }: { project: ProjectDetail; listId?: string; name: string; tasks: TaskItem[] }) {
  const [open, setOpen] = useState(true)
  const [title, setTitle] = useState('')
  const m = useTaskMutations(project.id)
  const toast = useToast()
  const can = project.permissions.canEditTasks
  return (
    <section className="overflow-hidden rounded-lg border border-border bg-surface">
      <button onClick={() => setOpen(!open)} className="flex w-full items-center gap-2 border-b border-border bg-surface-muted px-3 py-2 text-left text-sm font-semibold" aria-expanded={open}>
        <ChevronRight className={cn('size-4 transition-transform', open && 'rotate-90')} />
        {name}
        <span className="text-xs font-normal text-fg-muted">{tasks.length}</span>
      </button>
      {open && (
        <div className="md:overflow-x-auto">
          <table className="w-full text-sm md:min-w-[640px]">
            <thead className="hidden text-left text-xs text-fg-muted md:table-header-group">
              <tr className="border-b border-border">
                <th className="px-3 py-1.5 font-medium">Tarea</th>
                <th className="w-32 px-3 py-1.5 font-medium">Estado</th>
                <th className="w-40 px-3 py-1.5 font-medium">Responsable</th>
                <th className="w-28 px-3 py-1.5 font-medium">Vence</th>
                <th className="w-24 px-3 py-1.5 font-medium">Prioridad</th>
              </tr>
            </thead>
            <tbody className="block md:table-row-group">
              {tasks.map((t) => <TaskRow key={t.id} task={t} canEdit={can} />)}
              {can && listId && (
                <tr className="block md:table-row">
                  <td colSpan={5} className="block px-3 py-1 md:table-cell">
                    <div className="flex items-center gap-2">
                      <Plus className="size-4 text-fg-muted" />
                      <input
                        aria-label={`Nueva tarea en ${name}`}
                        placeholder="Nueva tarea (Enter para crear)"
                        className="w-full bg-transparent py-1.5 text-sm placeholder:text-fg-muted focus:outline-none touch:py-2.5 touch:text-base"
                        value={title}
                        onChange={(e) => setTitle(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key !== 'Enter' || !title.trim()) return
                          m.create.mutate({ listId, title: title.trim() }, { onSuccess: () => setTitle(''), onError: (err) => toast.error(errorMessage(err)) })
                        }}
                      />
                    </div>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

export function TaskRow({ task, canEdit, showProject }: { task: TaskItem; canEdit: boolean; showProject?: boolean }) {
  const { open } = useTaskDrawer()
  const m = useTaskMutations(task.projectId)
  const toast = useToast()
  return (
    <tr className="group flex flex-wrap items-center gap-x-3 gap-y-1.5 border-b border-border px-3 py-2.5 last:border-0 hover:bg-surface-muted/60 md:table-row md:p-0">
      <td className="block basis-full md:table-cell md:px-3 md:py-2">
        <div className="flex items-center gap-2">
          <input
            type="checkbox"
            className="size-4 shrink-0 accent-success-solid touch:size-5"
            aria-label={`Completar ${task.title}`}
            checked={task.status === 'Completada'}
            disabled={!canEdit}
            onChange={(e) => m.status.mutate({ id: task.id, status: e.target.checked ? 'Completada' : 'Pendiente' }, { onError: (err) => toast.error(errorMessage(err)) })}
          />
          <div className="min-w-0">
            <button onClick={() => open(task.id)} className={cn('block max-w-full text-left font-medium hover:text-primary-fg md:truncate', task.status === 'Completada' && 'text-fg-muted line-through')}>
              {task.title}
            </button>
            <div className="flex flex-wrap items-center gap-1.5 text-xs text-fg-muted">
              {showProject && <span>{task.projectName} · {task.listName}</span>}
              {task.subtaskCount > 0 && <span>☑ {task.completedSubtasks}/{task.subtaskCount}</span>}
              {task.attachmentCount > 0 && <span className="inline-flex items-center gap-0.5"><Paperclip className="size-3" />{task.attachmentCount}</span>}
              {task.commentCount > 0 && <span className="inline-flex items-center gap-0.5"><MessageSquare className="size-3" />{task.commentCount}</span>}
              {task.tags.map((tg) => <Badge key={tg.id} tone={tg.color as Tone} className="py-0">{tg.name}</Badge>)}
            </div>
          </div>
        </div>
      </td>
      <td className="block pl-6 md:table-cell md:px-3 md:py-2"><StatusBadge status={task.status} overdue={task.isOverdue} /></td>
      <td className="block md:table-cell md:px-3 md:py-2">
        <span className="flex items-center gap-2">
          <Avatar name={task.assigneeName} size="sm" />
          <span className="hidden truncate text-xs md:inline">{task.assigneeName ?? <span className="text-fg-muted">Sin asignar</span>}</span>
        </span>
      </td>
      <td className={cn('block text-xs md:table-cell md:px-3 md:py-2', task.isOverdue ? 'font-medium text-danger-fg' : 'text-fg-muted')}>{fmtShortDate(task.dueDate)}</td>
      <td className="block md:table-cell md:px-3 md:py-2"><PriorityBadge priority={task.priority} /></td>
    </tr>
  )
}
