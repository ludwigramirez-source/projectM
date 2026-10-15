import type { ProjectDetail } from '@/api/projects'
import { useTasks } from '@/api/tasks'
import { PageLoader } from '@/components/ui'
import { TaskToolbar } from './TaskToolbar'
import { ListView } from './ListView'
import { useTaskFilters } from './useTaskFilters'

/** Pestaña "Tareas" del proyecto: barra de filtros + vista seleccionada. */
export function ProjectTasks({ project }: { project: ProjectDetail }) {
  const { filters, view } = useTaskFilters()
  const { data, isLoading } = useTasks({ ...filters, projectId: project.id, pageSize: 100 })
  const tasks = data?.items ?? []
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
        </>
      )}
    </div>
  )
}
