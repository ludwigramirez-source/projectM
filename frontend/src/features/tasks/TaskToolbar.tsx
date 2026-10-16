import { useRef, useState } from 'react'
import { CalendarDays, Columns3, FilterX, List, Search, SlidersHorizontal } from 'lucide-react'
import type { ProjectDetail } from '@/api/projects'
import { useTags } from '@/api/tasks'
import { Button, Input, Select } from '@/components/ui'
import { cn } from '@/lib/cn'
import { PRIORITIES, TASK_STATUSES, priorityMeta, taskStatusMeta } from '@/lib/status'
import { useTaskFilters, type TaskView } from './useTaskFilters'

const VIEWS: { value: TaskView; label: string; icon: typeof List }[] = [
  { value: 'lista', label: 'Lista', icon: List },
  { value: 'kanban', label: 'Kanban', icon: Columns3 },
  { value: 'calendario', label: 'Calendario', icon: CalendarDays },
]

export function TaskToolbar({ project }: { project: ProjectDetail }) {
  const { filters, view, set, activeCount, clear } = useTaskFilters()
  const { data: tags } = useTags()
  const debounce = useRef<ReturnType<typeof setTimeout>>(undefined)
  const [open, setOpen] = useState(false)
  const mobileCount = activeCount - (filters.q ? 1 : 0) + (filters.listId ? 1 : 0)
  const search = (q: string) => {
    clearTimeout(debounce.current)
    debounce.current = setTimeout(() => set({ q }), 300)
  }
  return (
    <div className="flex flex-wrap items-center gap-2 border-b border-border bg-surface px-4 py-2 sm:px-6">
      <div role="tablist" aria-label="Vista" className="flex rounded-md border border-border p-0.5">
        {VIEWS.map((v) => (
          <button
            key={v.value}
            role="tab"
            aria-selected={view === v.value}
            onClick={() => set({ view: v.value === 'lista' ? null : v.value })}
            className={cn(
              'inline-flex items-center gap-1.5 rounded px-2.5 py-1 text-sm touch:px-3 touch:py-2',
              view === v.value ? 'bg-primary-subtle font-medium text-primary-fg' : 'text-fg-muted hover:text-fg',
            )}
          >
            <v.icon className="size-4" /> <span className="hidden sm:inline">{v.label}</span>
          </button>
        ))}
      </div>
      <div className="relative min-w-0 flex-1 md:max-w-64">
        <Search className="pointer-events-none absolute top-2.5 left-2.5 size-4 text-fg-muted" />
        <Input aria-label="Buscar tareas" placeholder="Buscar" className="pl-8" defaultValue={filters.q ?? ''}
          onChange={(e) => search(e.target.value)} />
      </div>
      <Button
        variant="secondary"
        className="md:hidden"
        aria-expanded={open}
        aria-controls="task-filters"
        icon={<SlidersHorizontal className="size-4" />}
        onClick={() => setOpen((o) => !o)}
      >
        Filtros{mobileCount > 0 && ` (${mobileCount})`}
      </Button>
      {/* En móvil los filtros se pliegan en un panel; desde md se muestran en línea. */}
      <div id="task-filters" className={cn('w-full grid-cols-2 gap-2 md:contents', open ? 'grid' : 'hidden')}>
      <Select aria-label="Lista" value={filters.listId ?? ''} onChange={(e) => set({ list: e.target.value })} wrapperClassName="md:w-36">
        <option value="">Todas las listas</option>
        {project.lists.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
      </Select>
      {view !== 'kanban' && (
        <Select aria-label="Estado" value={filters.status?.join(',') ?? ''} onChange={(e) => set({ status: e.target.value })} wrapperClassName="md:w-36">
          <option value="">Todos los estados</option>
          {TASK_STATUSES.map((s) => <option key={s} value={s}>{taskStatusMeta[s].label}</option>)}
          <option value="Pendiente,EnProgreso,EnRevision,Bloqueada">Abiertas</option>
        </Select>
      )}
      <Select aria-label="Responsable" value={filters.assigneeId ?? ''} onChange={(e) => set({ assignee: e.target.value })} wrapperClassName="md:w-40">
        <option value="">Todos los responsables</option>
        {project.members.map((m) => <option key={m.userId} value={m.userId}>{m.fullName}</option>)}
      </Select>
      <Select aria-label="Prioridad" value={filters.priority ?? ''} onChange={(e) => set({ priority: e.target.value })} wrapperClassName="md:w-32">
        <option value="">Prioridad</option>
        {PRIORITIES.map((p) => <option key={p} value={p}>{priorityMeta[p].label}</option>)}
      </Select>
      <Select aria-label="Etiqueta" value={filters.tagId ?? ''} onChange={(e) => set({ tag: e.target.value })} wrapperClassName="md:w-32">
        <option value="">Etiqueta</option>
        {tags?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
      </Select>
      <label className="inline-flex items-center gap-1.5 text-sm">
        <input type="checkbox" className="size-4 touch:size-5 accent-danger-solid" checked={!!filters.overdue} onChange={(e) => set({ overdue: e.target.checked ? '1' : null })} />
        Vencidas
      </label>
      {view === 'lista' && (
        <Select aria-label="Ordenar" value={filters.sort ?? ''} onChange={(e) => set({ sort: e.target.value })} wrapperClassName="md:w-40">
          <option value="">Orden manual</option>
          <option value="dueDate">Vencimiento</option>
          <option value="priority">Prioridad</option>
          <option value="title">Título</option>
          <option value="-createdAt">Más recientes</option>
        </Select>
      )}
      {activeCount > 0 && (
        <Button variant="ghost" size="sm" icon={<FilterX className="size-4" />} onClick={clear}>
          Limpiar ({activeCount})
        </Button>
      )}
      </div>
    </div>
  )
}
