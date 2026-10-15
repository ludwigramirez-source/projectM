import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Paged, type Role } from './client'

export interface UserDto {
  id: string
  email: string
  fullName: string
  role: Role
  isActive: boolean
  createdAt: string
}
export interface UserLookup {
  id: string
  fullName: string
  email: string
}

export interface UserFilters {
  q?: string
  role?: string
  page?: number
}

export const useUsers = (f: UserFilters) =>
  useQuery({
    queryKey: ['users', f],
    queryFn: ({ signal }) => api<Paged<UserDto>>('/users', { query: { ...f, pageSize: 20 }, signal }),
    placeholderData: keepPreviousData,
  })

export const useUserLookup = () =>
  useQuery({
    queryKey: ['users', 'lookup'],
    queryFn: ({ signal }) => api<UserLookup[]>('/users/lookup', { signal }),
    staleTime: 60_000,
  })

export function useSaveUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (u: { id?: string; email: string; fullName: string; role: Role; isActive?: boolean; password?: string }) =>
      u.id
        ? api<UserDto>(`/users/${u.id}`, { method: 'PUT', body: { ...u, isActive: u.isActive ?? true } })
        : api<UserDto>('/users', { method: 'POST', body: u }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }),
  })
}

export function useDeactivateUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api(`/users/${id}/deactivate`, { method: 'POST' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }),
  })
}

export const useResetPassword = () =>
  useMutation({
    mutationFn: ({ id, newPassword }: { id: string; newPassword: string }) =>
      api(`/users/${id}/reset-password`, { method: 'POST', body: { newPassword } }),
  })
