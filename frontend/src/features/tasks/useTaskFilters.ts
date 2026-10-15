import { useSearchParams } from 'react-router-dom'
import type { TaskFilters } from '@/api/tasks'
import type { Priority, TaskStatus } from '@/lib/status'

export type TaskView = 'lista' | 'kanban' | 'calendario'

/** Filtros y vista de tareas sincronizados con la URL (compartibles y persistentes al recargar). */
export function useTaskFilters() {
  const [params, setParams] = useSearchParams()
  const get = (k: string) => params.get(k) ?? undefined

  const filters: TaskFilters = {
    listId: get('list'),
    q: get('q'),
    status: (get('status')?.split(',').filter(Boolean) as TaskStatus[] | undefined) ?? undefined,
    assigneeId: get('assignee'),
    priority: get('priority') as Priority | undefined,
    tagId: get('tag'),
    overdue: params.get('overdue') === '1' || undefined,
    sort: get('sort'),
  }
  const view = (get('view') as TaskView | undefined) ?? 'lista'

  const set = (patch: Record<string, string | undefined | null>) => {
    const next = new URLSearchParams(params)
    for (const [k, v] of Object.entries(patch)) {
      if (v == null || v === '') next.delete(k)
      else next.set(k, v)
    }
    setParams(next, { replace: true })
  }

  const activeCount = ['q', 'status', 'assignee', 'priority', 'tag', 'overdue'].filter((k) => params.has(k)).length
  const clear = () => set({ q: null, status: null, assignee: null, priority: null, tag: null, overdue: null })

  return { filters, view, set, activeCount, clear }
}
