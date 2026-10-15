import { screen, within } from '@testing-library/react'
import { renderWithProviders } from '@/test/utils'
import { StatusBadge } from '@/components/ui'
import type { ProjectDetail } from '@/api/projects'
import type { TaskItem } from '@/api/tasks'
import { KanbanView } from './KanbanView'

const project = {
  id: 'p1',
  lists: [{ id: 'l1', name: 'General', position: 0, taskCount: 3 }],
  members: [],
  permissions: { canEditTasks: true, canManage: true, canClose: true, canReopen: false },
} as unknown as ProjectDetail

const task = (id: string, title: string, status: TaskItem['status'], position: number, extra: Partial<TaskItem> = {}): TaskItem => ({
  id,
  projectId: 'p1',
  projectName: 'Demo',
  listId: 'l1',
  listName: 'General',
  title,
  status,
  priority: 'Normal',
  position,
  subtaskCount: 0,
  completedSubtasks: 0,
  attachmentCount: 0,
  commentCount: 0,
  isOverdue: false,
  tags: [],
  ...extra,
})

describe('KanbanView', () => {
  it('agrupa las tarjetas por estado respetando el orden', () => {
    renderWithProviders(
      <KanbanView
        project={project}
        tasks={[
          task('b', 'Segunda', 'Pendiente', 2000),
          task('a', 'Primera', 'Pendiente', 1000),
          task('c', 'Revisar', 'EnRevision', 1000),
          task('d', 'Atrasada', 'EnProgreso', 1000, { isOverdue: true, dueDate: '2026-01-01' }),
        ]}
      />,
    )
    const pendiente = within(screen.getByRole('region', { name: 'Columna Pendiente' }))
    const titles = pendiente.getAllByRole('article').map((a) => within(a).getByText(/Primera|Segunda/).textContent)
    expect(titles).toEqual(['Primera', 'Segunda'])
    expect(within(screen.getByRole('region', { name: 'Columna En revisión' })).getByText('Revisar')).toBeInTheDocument()
    expect(screen.getAllByRole('region')).toHaveLength(5)
  })

  it('oculta el alta rápida cuando no hay permiso de edición', () => {
    renderWithProviders(
      <KanbanView project={{ ...project, permissions: { ...project.permissions, canEditTasks: false } }} tasks={[]} />,
    )
    expect(screen.queryByRole('button', { name: /Nueva tarea/ })).not.toBeInTheDocument()
  })
})

describe('StatusBadge', () => {
  it('muestra "Vencida" para tareas abiertas vencidas y el estado si está completada', () => {
    const { rerender } = renderWithProviders(<StatusBadge status="EnProgreso" overdue />)
    expect(screen.getByText('Vencida')).toBeInTheDocument()
    rerender(<StatusBadge status="Completada" overdue />)
    expect(screen.getByText('Completada')).toBeInTheDocument()
  })
})
