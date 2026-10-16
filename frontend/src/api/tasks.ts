import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, buildUrl, getAccessToken, refreshSession, type Paged } from './client'
import { keys as projectKeys, type ListDto } from './projects'
import type { Priority, TaskStatus } from '@/lib/status'

export interface Tag {
  id: string
  name: string
  color: string
}

export interface TaskItem {
  id: string
  projectId: string
  projectName: string
  listId: string
  listName: string
  parentTaskId?: string | null
  title: string
  status: TaskStatus
  priority: Priority
  startDate?: string | null
  dueDate?: string | null
  assigneeId?: string | null
  assigneeName?: string | null
  milestoneId?: string | null
  position: number
  subtaskCount: number
  completedSubtasks: number
  attachmentCount: number
  commentCount: number
  isOverdue: boolean
  tags: Tag[]
}

export type CustomFieldType = 'Texto' | 'Numero' | 'Fecha' | 'Seleccion'
export interface CustomFieldDef {
  id: string
  name: string
  type: CustomFieldType
  options: string[]
  isRequired: boolean
  position: number
}
export interface CustomFieldValue {
  fieldId: string
  name: string
  type: CustomFieldType
  options: string[]
  isRequired: boolean
  value?: string | null
}
export interface Attachment {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  uploadedByName: string
  createdAt: string
}
export interface Subtask {
  id: string
  title: string
  status: TaskStatus
  assigneeId?: string | null
  assigneeName?: string | null
  dueDate?: string | null
}

export interface TaskDetail {
  id: string
  projectId: string
  projectName: string
  listId: string
  listName: string
  parentTaskId?: string | null
  parentTitle?: string | null
  title: string
  description?: string | null
  status: TaskStatus
  blockedReason?: string | null
  priority: Priority
  startDate?: string | null
  dueDate?: string | null
  completedAt?: string | null
  assigneeId?: string | null
  assigneeName?: string | null
  milestoneId?: string | null
  milestoneName?: string | null
  createdByName: string
  createdAt: string
  updatedAt: string
  isOverdue: boolean
  rowVersion: string
  tags: Tag[]
  subtasks: Subtask[]
  customFields: CustomFieldValue[]
  attachments: Attachment[]
  canEdit: boolean
}

export interface TaskFilters {
  projectId?: string
  listId?: string
  status?: TaskStatus[]
  assigneeId?: string
  mine?: boolean
  priority?: Priority
  tagId?: string
  dueFrom?: string
  dueTo?: string
  overdue?: boolean
  q?: string
  includeSubtasks?: boolean
  sort?: string
  page?: number
  pageSize?: number
}

export const taskKeys = {
  list: (f: TaskFilters) => ['tasks', f] as const,
  detail: (id: string) => ['task', id] as const,
  fields: (projectId: string) => ['project', projectId, 'fields'] as const,
  lists: (projectId: string) => ['project', projectId, 'lists'] as const,
}

/** Refresca listados, detalle y métricas del proyecto tras un cambio en tareas. */
export function invalidateTasks(qc: QueryClient, projectId?: string, taskId?: string) {
  qc.invalidateQueries({ queryKey: ['tasks'] })
  if (taskId) qc.invalidateQueries({ queryKey: taskKeys.detail(taskId) })
  if (projectId) {
    qc.invalidateQueries({ queryKey: projectKeys.summary(projectId) })
    qc.invalidateQueries({ queryKey: projectKeys.blockers(projectId) })
    qc.invalidateQueries({ queryKey: projectKeys.closure(projectId) })
    qc.invalidateQueries({ queryKey: projectKeys.project(projectId) })
    qc.invalidateQueries({ queryKey: ['calendar'] })
  }
}

export const useTasks = (f: TaskFilters, enabled = true) =>
  useQuery({
    queryKey: taskKeys.list(f),
    queryFn: ({ signal }) => api<Paged<TaskItem>>('/tasks', { query: { ...f, pageSize: f.pageSize ?? 100 } as never, signal }),
    placeholderData: keepPreviousData,
    enabled,
  })

export const useTask = (id?: string | null) =>
  useQuery({
    queryKey: taskKeys.detail(id ?? ''),
    queryFn: ({ signal }) => api<TaskDetail>(`/tasks/${id}`, { signal }),
    enabled: !!id,
  })

export const useTags = () => useQuery({ queryKey: ['tags'], queryFn: ({ signal }) => api<Tag[]>('/tags', { signal }), staleTime: 60_000 })

export const useCustomFields = (projectId: string) =>
  useQuery({ queryKey: taskKeys.fields(projectId), queryFn: ({ signal }) => api<CustomFieldDef[]>(`/projects/${projectId}/custom-fields`, { signal }) })

export interface CreateTaskInput {
  listId: string
  title: string
  parentTaskId?: string | null
  description?: string
  assigneeId?: string | null
  status?: TaskStatus
  priority?: Priority
  startDate?: string | null
  dueDate?: string | null
  milestoneId?: string | null
  tagIds?: string[]
}

export function useTaskMutations(projectId?: string) {
  const qc = useQueryClient()
  const done = (d?: TaskDetail) => {
    if (d) qc.setQueryData(taskKeys.detail(d.id), d)
    invalidateTasks(qc, projectId ?? d?.projectId, d?.parentTaskId ?? undefined)
  }
  return {
    create: useMutation({ mutationFn: (t: CreateTaskInput) => api<TaskDetail>('/tasks', { method: 'POST', body: t }), onSuccess: done }),
    update: useMutation({
      mutationFn: (t: Pick<TaskDetail, 'id' | 'title' | 'description' | 'assigneeId' | 'priority' | 'startDate' | 'dueDate' | 'milestoneId' | 'rowVersion'>) =>
        api<TaskDetail>(`/tasks/${t.id}`, { method: 'PUT', body: t }),
      onSuccess: done,
    }),
    status: useMutation({
      mutationFn: (b: { id: string; status: TaskStatus; blockedReason?: string | null }) =>
        api<TaskDetail>(`/tasks/${b.id}/status`, { method: 'PATCH', body: b }),
      onSuccess: done,
    }),
    remove: useMutation({
      mutationFn: (id: string) => api(`/tasks/${id}`, { method: 'DELETE' }),
      onSuccess: () => invalidateTasks(qc, projectId),
    }),
    tags: useMutation({
      mutationFn: (b: { id: string; tagIds: string[] }) => api<TaskDetail>(`/tasks/${b.id}/tags`, { method: 'PUT', body: { tagIds: b.tagIds } }),
      onSuccess: done,
    }),
    field: useMutation({
      mutationFn: (b: { id: string; fieldId: string; value: string | null }) =>
        api<TaskDetail>(`/tasks/${b.id}/custom-fields/${b.fieldId}`, { method: 'PUT', body: { value: b.value } }),
      onSuccess: done,
    }),
    upload: useMutation({
      mutationFn: (b: { id: string; file: File }) => {
        const form = new FormData()
        form.append('file', b.file)
        return api<Attachment>(`/tasks/${b.id}/attachments`, { method: 'POST', body: form, direct: true })
      },
      onSuccess: (_, b) => invalidateTasks(qc, projectId, b.id),
    }),
    removeAttachment: useMutation({
      mutationFn: (b: { id: string; taskId: string }) => api(`/attachments/${b.id}`, { method: 'DELETE' }),
      onSuccess: (_, b) => invalidateTasks(qc, projectId, b.taskId),
    }),
  }
}

export interface MoveInput {
  id: string
  listId?: string
  status?: TaskStatus
  blockedReason?: string
  beforeId?: string | null
  afterId?: string | null
}

/** Mover con actualización optimista (Kanban/Lista); revierte si el servidor rechaza. */
export function useMoveTask(projectId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (m: MoveInput) => api<TaskItem>(`/tasks/${m.id}/move`, { method: 'PATCH', body: m }),
    onMutate: async (m) => {
      await qc.cancelQueries({ queryKey: ['tasks'] })
      const snapshot = qc.getQueriesData<Paged<TaskItem>>({ queryKey: ['tasks'] })
      qc.setQueriesData<Paged<TaskItem>>({ queryKey: ['tasks'] }, (old) => {
        if (!old) return old
        const items = [...old.items]
        const i = items.findIndex((t) => t.id === m.id)
        if (i < 0) return old
        const before = m.beforeId ? items.find((t) => t.id === m.beforeId)?.position : undefined
        const after = m.afterId ? items.find((t) => t.id === m.afterId)?.position : undefined
        const position =
          before != null && after != null ? (before + after) / 2 : before != null ? before + 1000 : after != null ? after - 1000 : items[i]!.position
        items[i] = { ...items[i]!, status: m.status ?? items[i]!.status, listId: m.listId ?? items[i]!.listId, position }
        items.sort((a, b) => a.position - b.position)
        return { ...old, items }
      })
      return { snapshot }
    },
    onError: (_e, _m, ctx) => ctx?.snapshot.forEach(([key, data]) => qc.setQueryData(key, data)),
    onSettled: (_d, _e, m) => invalidateTasks(qc, projectId, m.id),
  })
}

export function useCreateTag() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (t: { name: string; color: string }) => api<Tag>('/tags', { method: 'POST', body: t }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tags'] }),
  })
}

export function useFieldMutations(projectId: string) {
  const qc = useQueryClient()
  const onSuccess = () => {
    qc.invalidateQueries({ queryKey: taskKeys.fields(projectId) })
    qc.invalidateQueries({ queryKey: ['task'] })
  }
  return {
    save: useMutation({
      mutationFn: (f: { id?: string; name: string; type: CustomFieldType; options: string[]; isRequired: boolean }) =>
        f.id ? api<CustomFieldDef>(`/custom-fields/${f.id}`, { method: 'PUT', body: f }) : api<CustomFieldDef>(`/projects/${projectId}/custom-fields`, { method: 'POST', body: f }),
      onSuccess,
    }),
    remove: useMutation({ mutationFn: (id: string) => api(`/custom-fields/${id}`, { method: 'DELETE' }), onSuccess }),
  }
}

export function useListMutations(projectId: string) {
  const qc = useQueryClient()
  const onSuccess = () => {
    qc.invalidateQueries({ queryKey: projectKeys.project(projectId) })
    qc.invalidateQueries({ queryKey: projectKeys.tree })
  }
  return {
    create: useMutation({ mutationFn: (name: string) => api<ListDto>(`/projects/${projectId}/lists`, { method: 'POST', body: { name } }), onSuccess }),
    rename: useMutation({ mutationFn: (b: { id: string; name: string }) => api<ListDto>(`/lists/${b.id}`, { method: 'PUT', body: { name: b.name } }), onSuccess }),
    remove: useMutation({ mutationFn: (id: string) => api(`/lists/${id}`, { method: 'DELETE' }), onSuccess }),
  }
}

/** Descarga un adjunto con el token (los enlaces directos no llevan Authorization). */
export async function downloadAttachment(a: Attachment) {
  const go = () => fetch(buildUrl(`/attachments/${a.id}/download`, undefined, true), { headers: { Authorization: `Bearer ${getAccessToken()}` } })
  let res = await go()
  if (res.status === 401 && (await refreshSession())) res = await go()
  if (!res.ok) throw new Error('No se pudo descargar el archivo.')
  const url = URL.createObjectURL(await res.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = a.fileName
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
