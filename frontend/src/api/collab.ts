import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Paged } from './client'
import { invalidateTasks } from './tasks'

export interface Comment {
  id: string
  taskId: string
  authorId: string
  authorName: string
  body: string
  mentions: string[]
  createdAt: string
  editedAt?: string | null
  canEdit: boolean
}

export interface Activity {
  id: string
  projectId: string
  taskId?: string | null
  actorId?: string | null
  actorName: string
  type: string
  message: string
  data?: string | null
  createdAt: string
}

export interface AppNotification {
  id: string
  type: string
  title: string
  body?: string | null
  link?: string | null
  projectId?: string | null
  taskId?: string | null
  createdAt: string
  readAt?: string | null
}

export const collabKeys = {
  comments: (taskId: string) => ['task', taskId, 'comments'] as const,
  taskActivity: (taskId: string) => ['task', taskId, 'activity'] as const,
  projectActivity: (projectId: string, page: number) => ['project', projectId, 'activity', page] as const,
  notifications: ['notifications'] as const,
  unread: ['notifications', 'unread'] as const,
}

export const useComments = (taskId: string) =>
  useQuery({ queryKey: collabKeys.comments(taskId), queryFn: ({ signal }) => api<Paged<Comment>>(`/tasks/${taskId}/comments`, { signal }) })

export function useCommentMutations(taskId: string, projectId: string) {
  const qc = useQueryClient()
  const onSuccess = () => {
    qc.invalidateQueries({ queryKey: collabKeys.comments(taskId) })
    qc.invalidateQueries({ queryKey: collabKeys.taskActivity(taskId) })
    invalidateTasks(qc, projectId)
  }
  return {
    add: useMutation({
      mutationFn: (b: { body: string; mentionIds: string[] }) => api<Comment>(`/tasks/${taskId}/comments`, { method: 'POST', body: b }),
      onSuccess,
    }),
    edit: useMutation({
      mutationFn: (b: { id: string; body: string; mentionIds: string[] }) => api<Comment>(`/comments/${b.id}`, { method: 'PUT', body: b }),
      onSuccess,
    }),
    remove: useMutation({ mutationFn: (id: string) => api(`/comments/${id}`, { method: 'DELETE' }), onSuccess }),
  }
}

export const useTaskActivity = (taskId: string) =>
  useQuery({ queryKey: collabKeys.taskActivity(taskId), queryFn: ({ signal }) => api<Activity[]>(`/tasks/${taskId}/activity`, { signal }) })

export const useProjectActivity = (projectId: string, page = 1, pageSize = 30) =>
  useQuery({
    queryKey: collabKeys.projectActivity(projectId, page),
    queryFn: ({ signal }) => api<Paged<Activity>>(`/projects/${projectId}/activity`, { query: { page, pageSize }, signal }),
    placeholderData: keepPreviousData,
  })

export const useNotifications = (enabled: boolean) =>
  useQuery({
    queryKey: collabKeys.notifications,
    queryFn: ({ signal }) => api<Paged<AppNotification>>('/notifications', { query: { pageSize: 30 }, signal }),
    enabled,
  })

export const useUnreadCount = () =>
  useQuery({
    queryKey: collabKeys.unread,
    queryFn: ({ signal }) => api<{ count: number }>('/notifications/unread-count', { signal }),
    // Respaldo si la conexión en tiempo real se cae.
    refetchInterval: 120_000,
  })

export function useNotificationMutations() {
  const qc = useQueryClient()
  const onSuccess = () => qc.invalidateQueries({ queryKey: collabKeys.notifications })
  return {
    read: useMutation({ mutationFn: (id: string) => api(`/notifications/${id}/read`, { method: 'POST' }), onSuccess }),
    readAll: useMutation({ mutationFn: () => api('/notifications/read-all', { method: 'POST' }), onSuccess }),
  }
}
