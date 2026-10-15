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
  secondary: 'bg-secondary-100 text-secondary-800',
  primary: 'bg-primary-100 text-primary-800',
  accent: 'bg-accent-100 text-accent-900',
  success: 'bg-success-100 text-success-800',
  danger: 'bg-danger-50 text-danger-700',
  neutral: 'bg-neutral-100 text-neutral-700',
}

/** Color sólido (puntos, bordes de columna Kanban, barras). */
export const toneDot: Record<Tone, string> = {
  secondary: 'bg-secondary-500',
  primary: 'bg-primary-500',
  accent: 'bg-accent-500',
  success: 'bg-success-500',
  danger: 'bg-danger-500',
  neutral: 'bg-neutral-400',
}

export const toneBorder: Record<Tone, string> = {
  secondary: 'border-secondary-500',
  primary: 'border-primary-500',
  accent: 'border-accent-500',
  success: 'border-success-500',
  danger: 'border-danger-500',
  neutral: 'border-neutral-400',
}

/** Variable CSS del tono (para SVG/gráficos). */
export const toneVar: Record<Tone, string> = {
  secondary: 'rgb(var(--pm-secondary-500))',
  primary: 'rgb(var(--pm-primary-500))',
  accent: 'rgb(var(--pm-accent-500))',
  success: 'rgb(var(--pm-success-500))',
  danger: 'rgb(var(--pm-danger-500))',
  neutral: 'rgb(var(--pm-neutral-400))',
}
