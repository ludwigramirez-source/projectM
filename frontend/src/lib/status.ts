/** Catálogos de estados/prioridades y su mapeo a tokens de la paleta. */

export type TaskStatus = 'Pendiente' | 'EnProgreso' | 'EnRevision' | 'Completada' | 'Bloqueada'
export type Priority = 'Baja' | 'Normal' | 'Alta' | 'Urgente'
export type ProjectStatus = 'Planeacion' | 'Activo' | 'EnPausa' | 'EnCierre' | 'Cerrado'
export type ProjectHealth = 'EnTiempo' | 'EnRiesgo' | 'Retrasado'

export type Tone = 'secondary' | 'primary' | 'accent' | 'success' | 'danger' | 'neutral'

export const TASK_STATUSES: TaskStatus[] = ['Pendiente', 'EnProgreso', 'EnRevision', 'Completada', 'Bloqueada']
export const PRIORITIES: Priority[] = ['Urgente', 'Alta', 'Normal', 'Baja']

export const taskStatusMeta: Record<TaskStatus, { label: string; tone: Tone }> = {
  Pendiente: { label: 'Pendiente', tone: 'secondary' },
  EnProgreso: { label: 'En progreso', tone: 'primary' },
  EnRevision: { label: 'En revisión', tone: 'accent' },
  Completada: { label: 'Completada', tone: 'success' },
  Bloqueada: { label: 'Bloqueada', tone: 'danger' },
}

export const priorityMeta: Record<Priority, { label: string; tone: Tone }> = {
  Urgente: { label: 'Urgente', tone: 'danger' },
  Alta: { label: 'Alta', tone: 'primary' },
  Normal: { label: 'Normal', tone: 'secondary' },
  Baja: { label: 'Baja', tone: 'neutral' },
}

export const projectStatusMeta: Record<ProjectStatus, { label: string; tone: Tone }> = {
  Planeacion: { label: 'Planeación', tone: 'secondary' },
  Activo: { label: 'Activo', tone: 'primary' },
  EnPausa: { label: 'En pausa', tone: 'neutral' },
  EnCierre: { label: 'En cierre', tone: 'accent' },
  Cerrado: { label: 'Cerrado', tone: 'success' },
}

export const healthMeta: Record<ProjectHealth, { label: string; tone: Tone }> = {
  EnTiempo: { label: 'En tiempo', tone: 'success' },
  EnRiesgo: { label: 'En riesgo', tone: 'accent' },
  Retrasado: { label: 'Retrasado', tone: 'danger' },
}

/** Clases de badge (fondo claro + texto oscuro, AA verificado). */
export const toneBadge: Record<Tone, string> = {
  secondary: 'bg-secondary-subtle text-secondary-subtle-fg',
  primary: 'bg-primary-subtle text-primary-subtle-fg',
  accent: 'bg-accent-subtle text-accent-subtle-fg',
  success: 'bg-success-subtle text-success-subtle-fg',
  danger: 'bg-danger-subtle text-danger-subtle-fg',
  neutral: 'bg-neutral-subtle text-neutral-subtle-fg',
}

/** Color sólido (puntos, bordes de columna Kanban, barras). */
export const toneDot: Record<Tone, string> = {
  secondary: 'bg-secondary-mark',
  primary: 'bg-primary-mark',
  accent: 'bg-accent-mark',
  success: 'bg-success-mark',
  danger: 'bg-danger-mark',
  neutral: 'bg-neutral-mark',
}

export const toneBorder: Record<Tone, string> = {
  secondary: 'border-secondary-mark',
  primary: 'border-primary-mark',
  accent: 'border-accent-mark',
  success: 'border-success-mark',
  danger: 'border-danger-mark',
  neutral: 'border-neutral-mark',
}

