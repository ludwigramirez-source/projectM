import type { ProjectDetail } from '@/api/projects'
import { useTasks } from '@/api/tasks'
import { PageLoader } from '@/components/ui'
import { TaskToolbar } from './TaskToolbar'
import { ListView } from './ListView'
import { KanbanView } from './KanbanView'
import { CalendarView } from './CalendarView'
import { useTaskFilters } from './useTaskFilters'

/** Pestaña "Tareas" del proyecto: barra de filtros + vista seleccionada (Lista, Kanban o Calendario). */
export function ProjectTasks({ project }: { project: ProjectDetail }) {
  const { filters, view } = useTaskFilters()
  // En Kanban las columnas son los estados: el filtro de estado no aplica.
  const effective = view === 'kanban' ? { ...filters, status: undefined, sort: undefined } : filters
  const { data, isLoading } = useTasks({ ...effective, projectId: project.id, pageSize: 100 }, view !== 'calendario')
  const tasks = data?.items ?? []

  if (view === 'calendario')
    return (
      <div>
        <TaskToolbar project={project} />
        <CalendarView projectId={project.id} filters={filters} canEdit={project.permissions.canEditTasks} />
      </div>
    )

  return (
    <div>
      <TaskToolbar project={project} />
      {isLoading ? (
        <PageLoader />
      ) : (
        <>
          {data && data.totalCount > data.items.length && (
            <p className="px-6 pt-3 text-xs text-fg-muted">
              Mostrando {data.items.length} de {data.totalCount} tareas. Usa los filtros para acotar.
            </p>
          )}
          {view === 'lista' && <ListView project={project} tasks={tasks} grouped={!filters.listId && !filters.sort} />}
          {view === 'kanban' && <KanbanView project={project} tasks={tasks} listId={filters.listId} />}
        </>
      )}
    </div>
  )
}
