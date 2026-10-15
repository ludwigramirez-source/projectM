import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Paged, type Role } from './client'
import type { Priority, ProjectHealth, ProjectStatus, TaskStatus } from '@/lib/status'

// ---------------- Tipos ----------------

export interface SpaceDto {
  id: string
  name: string
  description?: string | null
  color: string
  projectCount: number
}
export interface TreeSpace {
  id: string
  name: string
  color: string
  projects: { id: string; code: string; name: string; status: ProjectStatus; lists: { id: string; name: string }[] }[]
}
export interface ClientDto {
  id: string
  name: string
}

export interface ProjectListItem {
  id: string
  code: string
  name: string
  spaceId: string
  spaceName: string
  clientName?: string | null
  status: ProjectStatus
  health: ProjectHealth
  managerId: string
  managerName: string
  plannedStart: string
  plannedEnd: string
  totalTasks: number
  completedTasks: number
  overdueTasks: number
  progress: number
}

export interface MemberDto {
  userId: string
  fullName: string
  email: string
  role: Role
  isManager: boolean
}
export interface ListDto {
  id: string
  name: string
  position: number
  taskCount: number
}
export interface ProjectPermissions {
  canEditTasks: boolean
  canManage: boolean
  canClose: boolean
  canReopen: boolean
}
export interface ProjectDetail {
  id: string
  code: string
  name: string
  spaceId: string
  spaceName: string
  clientId?: string | null
  clientName?: string | null
  objective?: string | null
  scope?: string | null
  plannedStart: string
  plannedEnd: string
  actualStart?: string | null
  actualEnd?: string | null
  budget?: number | null
  currency: string
  managerId: string
  managerName: string
  status: ProjectStatus
  health: ProjectHealth
  createdAt: string
  closedAt?: string | null
  rowVersion: string
  members: MemberDto[]
  lists: ListDto[]
  permissions: ProjectPermissions
}

export interface MilestoneSummary {
  id: string
  name: string
  dueDate: string
  completedAt?: string | null
  totalTasks: number
  completedTasks: number
  isOverdue: boolean
}
export interface ProjectSummary {
  progress: number
  totalTasks: number
  completedTasks: number
  overdueTasks: number
  blockedTasks: number
  openRisks: number
  highRisks: number
  tasksByStatus: Record<TaskStatus, number>
  daysRemaining?: number | null
  milestones: MilestoneSummary[]
}

export interface TaskSummary {
  id: string
  projectId: string
  listId: string
  parentTaskId?: string | null
  title: string
  status: TaskStatus
  priority: Priority
  startDate?: string | null
  dueDate?: string | null
  assigneeId?: string | null
  assigneeName?: string | null
  blockedReason?: string | null
}

export interface Milestone {
  id: string
  name: string
  description?: string | null
  dueDate: string
  completedAt?: string | null
  totalTasks: number
  completedTasks: number
}
export type RiskStatus = 'Abierto' | 'Mitigado' | 'Cerrado'
export interface Risk {
  id: string
  title: string
  description?: string | null
  probability: number
  impact: number
  severity: number
  status: RiskStatus
  ownerId?: string | null
  ownerName?: string | null
  mitigation?: string | null
  createdAt: string
}
export interface Blockers {
  blockedTasks: TaskSummary[]
  overdueTasks: TaskSummary[]
  openRisks: Risk[]
}

export interface ChecklistItem {
  id: string
  text: string
  isRequired: boolean
  isDone: boolean
  doneByName?: string | null
  doneAt?: string | null
  notes?: string | null
}
export interface Lesson {
  id?: string | null
  category: string
  whatWentWell?: string | null
  whatToImprove?: string | null
  recommendation?: string | null
}
export interface Closure {
  status: ProjectStatus
  checklist: ChecklistItem[]
  summary?: string | null
  deliverables: string[]
  lessons: Lesson[]
  metrics: { totalTasks: number; completedTasks: number; openTasks: number; progress: number }
  plannedStart: string
  plannedEnd: string
  actualStart?: string | null
  actualEnd?: string | null
  isForced: boolean
  forceJustification?: string | null
  closedByName?: string | null
  closedAt?: string | null
  problems: { code: string; message: string }[]
}

export interface TemplateTask {
  listName: string
  title: string
  description?: string | null
  priority: Priority
  startOffsetDays: number
  durationDays: number
}
export interface TemplateListItem {
  id: string
  name: string
  description?: string | null
  taskCount: number
  listCount: number
}
export interface Template {
  id: string
  name: string
  description?: string | null
  tasks: TemplateTask[]
}
export interface ChecklistTemplateItem {
  id?: string | null
  text: string
  isRequired: boolean
}

// ---------------- Claves ----------------

export const keys = {
  tree: ['tree'] as const,
  spaces: ['spaces'] as const,
  projects: (f?: object) => ['projects', f ?? {}] as const,
  project: (id: string) => ['project', id] as const,
  summary: (id: string) => ['project', id, 'summary'] as const,
  milestones: (id: string) => ['project', id, 'milestones'] as const,
  risks: (id: string) => ['project', id, 'risks'] as const,
  blockers: (id: string) => ['project', id, 'blockers'] as const,
  closure: (id: string) => ['project', id, 'closure'] as const,
}

/** Invalida todo lo relacionado con un proyecto (detalle, resumen, árbol, listados). */
export function useInvalidateProject() {
  const qc = useQueryClient()
  return (id: string) => {
    qc.invalidateQueries({ queryKey: ['project', id] })
    qc.invalidateQueries({ queryKey: ['projects'] })
    qc.invalidateQueries({ queryKey: keys.tree })
  }
}

// ---------------- Espacios y clientes ----------------

export const useTree = () =>
  useQuery({ queryKey: keys.tree, queryFn: ({ signal }) => api<TreeSpace[]>('/spaces/tree', { signal }) })

export const useSpaces = () =>
  useQuery({ queryKey: keys.spaces, queryFn: ({ signal }) => api<SpaceDto[]>('/spaces', { signal }) })

export function useSaveSpace() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (s: { id?: string; name: string; description?: string; color: string }) =>
      s.id ? api<SpaceDto>(`/spaces/${s.id}`, { method: 'PUT', body: s }) : api<SpaceDto>('/spaces', { method: 'POST', body: s }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.spaces })
      qc.invalidateQueries({ queryKey: keys.tree })
    },
  })
}

export const useClients = () =>
  useQuery({ queryKey: ['clients'], queryFn: ({ signal }) => api<ClientDto[]>('/clients', { signal }), staleTime: 60_000 })

// ---------------- Proyectos ----------------

export interface ProjectFilters {
  spaceId?: string
  status?: string
  q?: string
  page?: number
}

export const useProjects = (f: ProjectFilters) =>
  useQuery({
    queryKey: keys.projects(f),
    queryFn: ({ signal }) => api<Paged<ProjectListItem>>('/projects', { query: { ...f, pageSize: 20 }, signal }),
    placeholderData: keepPreviousData,
  })

export const useProject = (id: string) =>
  useQuery({ queryKey: keys.project(id), queryFn: ({ signal }) => api<ProjectDetail>(`/projects/${id}`, { signal }) })

export const useProjectSummary = (id: string) =>
  useQuery({ queryKey: keys.summary(id), queryFn: ({ signal }) => api<ProjectSummary>(`/projects/${id}/summary`, { signal }) })

export interface CreateProjectInput {
  spaceId: string
  name: string
  clientId?: string | null
  clientName?: string | null
  objective?: string
  scope?: string
  plannedStart: string
  plannedEnd: string
  budget?: number | null
  currency?: string
  managerId?: string | null
  memberIds: string[]
  templateId?: string | null
  activateNow: boolean
}

export function useCreateProject() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (p: CreateProjectInput) => api<ProjectDetail>('/projects', { method: 'POST', body: p }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['projects'] })
      qc.invalidateQueries({ queryKey: keys.tree })
    },
  })
}

export function useUpdateProject(id: string) {
  const invalidate = useInvalidateProject()
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (p: Omit<CreateProjectInput, 'memberIds' | 'templateId' | 'activateNow'> & { managerId: string; health: ProjectHealth; rowVersion: string }) =>
      api<ProjectDetail>(`/projects/${id}`, { method: 'PUT', body: p }),
    onSuccess: (d) => {
      qc.setQueryData(keys.project(id), d)
      invalidate(id)
    },
  })
}

export function useProjectTransition(id: string) {
  const invalidate = useInvalidateProject()
  return useMutation({
    mutationFn: (action: 'activate' | 'pause' | 'resume') => api<ProjectDetail>(`/projects/${id}/${action}`, { method: 'POST' }),
    onSuccess: () => invalidate(id),
  })
}

export function useMemberMutations(id: string) {
  const invalidate = useInvalidateProject()
  const add = useMutation({
    mutationFn: (userId: string) => api<MemberDto[]>(`/projects/${id}/members`, { method: 'POST', body: { userId } }),
    onSuccess: () => invalidate(id),
  })
  const remove = useMutation({
    mutationFn: (userId: string) => api<MemberDto[]>(`/projects/${id}/members/${userId}`, { method: 'DELETE' }),
    onSuccess: () => invalidate(id),
  })
  return { add, remove }
}

// ---------------- Seguimiento ----------------

export const useMilestones = (id: string) =>
  useQuery({ queryKey: keys.milestones(id), queryFn: ({ signal }) => api<Milestone[]>(`/projects/${id}/milestones`, { signal }) })

export const useRisks = (id: string) =>
  useQuery({ queryKey: keys.risks(id), queryFn: ({ signal }) => api<Risk[]>(`/projects/${id}/risks`, { signal }) })

export const useBlockers = (id: string) =>
  useQuery({ queryKey: keys.blockers(id), queryFn: ({ signal }) => api<Blockers>(`/projects/${id}/blockers`, { signal }) })

export function useMilestoneMutations(id: string) {
  const invalidate = useInvalidateProject()
  const save = useMutation({
    mutationFn: (m: { id?: string; name: string; description?: string; dueDate: string; completed?: boolean }) =>
      m.id
        ? api<Milestone>(`/projects/${id}/milestones/${m.id}`, { method: 'PUT', body: m })
        : api<Milestone>(`/projects/${id}/milestones`, { method: 'POST', body: m }),
    onSuccess: () => invalidate(id),
  })
  const remove = useMutation({
    mutationFn: (mid: string) => api(`/projects/${id}/milestones/${mid}`, { method: 'DELETE' }),
    onSuccess: () => invalidate(id),
  })
  return { save, remove }
}

export function useRiskMutations(id: string) {
  const invalidate = useInvalidateProject()
  const save = useMutation({
    mutationFn: (r: Partial<Risk> & { title: string; probability: number; impact: number }) =>
      r.id
        ? api<Risk>(`/projects/${id}/risks/${r.id}`, { method: 'PUT', body: r })
        : api<Risk>(`/projects/${id}/risks`, { method: 'POST', body: r }),
    onSuccess: () => invalidate(id),
  })
  const remove = useMutation({
    mutationFn: (rid: string) => api(`/projects/${id}/risks/${rid}`, { method: 'DELETE' }),
    onSuccess: () => invalidate(id),
  })
  return { save, remove }
}

// ---------------- Cierre ----------------

export const useClosure = (id: string) =>
  useQuery({ queryKey: keys.closure(id), queryFn: ({ signal }) => api<Closure>(`/projects/${id}/closure`, { signal }) })

export function useClosureMutations(id: string) {
  const qc = useQueryClient()
  const invalidate = useInvalidateProject()
  const onSuccess = (c: Closure) => {
    qc.setQueryData(keys.closure(id), c)
    invalidate(id)
  }
  return {
    start: useMutation({ mutationFn: () => api<Closure>(`/projects/${id}/closure/start`, { method: 'POST' }), onSuccess }),
    cancel: useMutation({ mutationFn: () => api<Closure>(`/projects/${id}/closure/cancel`, { method: 'POST' }), onSuccess }),
    update: useMutation({
      mutationFn: (b: { summary?: string; deliverables?: string[]; lessons?: Lesson[] }) =>
        api<Closure>(`/projects/${id}/closure`, { method: 'PUT', body: b }),
      onSuccess,
    }),
    check: useMutation({
      mutationFn: (b: { itemId: string; isDone: boolean; notes?: string | null }) =>
        api<Closure>(`/projects/${id}/closure/checklist/${b.itemId}`, { method: 'PUT', body: b }),
      onSuccess,
    }),
    close: useMutation({
      mutationFn: (b: { force: boolean; justification?: string }) => api<Closure>(`/projects/${id}/close`, { method: 'POST', body: b }),
      onSuccess,
    }),
    reopen: useMutation({
      mutationFn: (reason: string) => api<Closure>(`/projects/${id}/reopen`, { method: 'POST', body: { reason } }),
      onSuccess,
    }),
  }
}

// ---------------- Plantillas ----------------

export const useTemplates = () =>
  useQuery({ queryKey: ['templates'], queryFn: ({ signal }) => api<TemplateListItem[]>('/project-templates', { signal }) })

export const useTemplate = (id?: string | null) =>
  useQuery({
    queryKey: ['templates', id],
    queryFn: ({ signal }) => api<Template>(`/project-templates/${id}`, { signal }),
    enabled: !!id,
  })

export function useTemplateMutations() {
  const qc = useQueryClient()
  const onSuccess = () => qc.invalidateQueries({ queryKey: ['templates'] })
  return {
    save: useMutation({
      mutationFn: (t: Omit<Template, 'id'> & { id?: string }) =>
        t.id ? api<Template>(`/project-templates/${t.id}`, { method: 'PUT', body: t }) : api<Template>('/project-templates', { method: 'POST', body: t }),
      onSuccess,
    }),
    remove: useMutation({ mutationFn: (id: string) => api(`/project-templates/${id}`, { method: 'DELETE' }), onSuccess }),
  }
}

export const useChecklistTemplate = () =>
  useQuery({ queryKey: ['checklist-template'], queryFn: ({ signal }) => api<ChecklistTemplateItem[]>('/closure-checklist-template', { signal }) })

export function useSaveChecklistTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (items: ChecklistTemplateItem[]) =>
      api<ChecklistTemplateItem[]>('/closure-checklist-template', { method: 'PUT', body: { items } }),
    onSuccess: (d) => qc.setQueryData(['checklist-template'], d),
  })
}
