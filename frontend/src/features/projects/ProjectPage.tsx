import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Lock, Pause, Play, RotateCcw } from 'lucide-react'
import { useProject, useProjectTransition, useClosureMutations, type ProjectDetail } from '@/api/projects'
import { errorMessage } from '@/api/client'
import {
  Button,
  EmptyState,
  HealthBadge,
  Modal,
  PageHeader,
  PageLoader,
  ProjectStatusBadge,
  Tabs,
  Textarea,
  useToast,
} from '@/components/ui'
import { OverviewTab } from './OverviewTab'
import { TrackingTab } from './TrackingTab'
import { TeamTab } from './TeamTab'
import { ClosureTab } from './ClosureTab'
import { SettingsTab } from './SettingsTab'
import { ProjectTasks } from '@/features/tasks/ProjectTasks'

export type ProjectTab = 'resumen' | 'tareas' | 'seguimiento' | 'equipo' | 'cierre' | 'ajustes'

export function ProjectPage() {
  const { projectId = '', tab = 'resumen' } = useParams()
  const navigate = useNavigate()
  const { data: project, isLoading, error } = useProject(projectId)

  if (isLoading) return <PageLoader />
  if (error || !project)
    return <EmptyState title="Proyecto no encontrado" description="No existe o no tienes acceso a este proyecto." />

  const perms = project.permissions
  const items: { value: ProjectTab; label: string }[] = [
    { value: 'resumen', label: 'Resumen' },
    { value: 'tareas', label: 'Tareas' },
    { value: 'seguimiento', label: 'Riesgos y bloqueos' },
    { value: 'equipo', label: `Equipo (${project.members.length})` },
    { value: 'cierre', label: project.status === 'Cerrado' ? 'Acta de cierre' : 'Cierre' },
  ]
  if (perms.canManage) items.push({ value: 'ajustes', label: 'Ajustes' })

  return (
    <div className="flex min-h-full flex-col">
      <PageHeader
        title={project.name}
        subtitle={
          <span className="flex flex-wrap items-center gap-2">
            <span>
              {project.code} · {project.spaceName}
              {project.clientName && ` · ${project.clientName}`}
            </span>
            <ProjectStatusBadge status={project.status} />
            {project.status !== 'Cerrado' && <HealthBadge health={project.health} />}
          </span>
        }
        actions={<LifecycleActions project={project} />}
      >
        <Tabs value={tab as ProjectTab} onChange={(t) => navigate(`/projects/${project.id}/${t === 'resumen' ? '' : t}`)} items={items} />
      </PageHeader>
      {project.status === 'Cerrado' && <ClosedBanner project={project} />}
      <div className="flex-1">
        {tab === 'resumen' && <OverviewTab project={project} />}
        {tab === 'tareas' && <ProjectTasks project={project} />}
        {tab === 'seguimiento' && <TrackingTab project={project} />}
        {tab === 'equipo' && <TeamTab project={project} />}
        {tab === 'cierre' && <ClosureTab project={project} />}
        {tab === 'ajustes' && perms.canManage && <SettingsTab project={project} />}
      </div>
    </div>
  )
}

function LifecycleActions({ project }: { project: ProjectDetail }) {
  const transition = useProjectTransition(project.id)
  const toast = useToast()
  if (!project.permissions.canManage) return null
  const run = (action: 'activate' | 'pause' | 'resume', msg: string) =>
    transition.mutate(action, { onSuccess: () => toast.success(msg), onError: (e) => toast.error(errorMessage(e)) })
  return (
    <>
      {project.status === 'Planeacion' && (
        <Button icon={<Play className="size-4" />} loading={transition.isPending} onClick={() => run('activate', 'Proyecto activado')}>
          Activar proyecto
        </Button>
      )}
      {project.status === 'Activo' && (
        <Button variant="secondary" icon={<Pause className="size-4" />} loading={transition.isPending} onClick={() => run('pause', 'Proyecto en pausa')}>
          Pausar
        </Button>
      )}
      {project.status === 'EnPausa' && (
        <Button icon={<Play className="size-4" />} loading={transition.isPending} onClick={() => run('resume', 'Proyecto reanudado')}>
          Reanudar
        </Button>
      )}
    </>
  )
}

function ClosedBanner({ project }: { project: ProjectDetail }) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const { reopen } = useClosureMutations(project.id)
  const toast = useToast()
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 border-b border-success-200 bg-success-50 px-4 py-2 text-sm text-success-800 sm:px-6">
      <span className="flex items-center gap-2">
        <Lock className="size-4" /> Proyecto cerrado: está en modo de solo lectura.
      </span>
      {project.permissions.canReopen && (
        <Button size="sm" variant="secondary" icon={<RotateCcw className="size-3.5" />} onClick={() => setOpen(true)}>
          Reabrir
        </Button>
      )}
      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Reabrir proyecto"
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)}>
              Cancelar
            </Button>
            <Button
              disabled={reason.trim().length < 10}
              loading={reopen.isPending}
              onClick={() =>
                reopen.mutate(reason, {
                  onSuccess: () => {
                    toast.success('Proyecto reabierto')
                    setOpen(false)
                  },
                  onError: (e) => toast.error(errorMessage(e)),
                })
              }
            >
              Reabrir
            </Button>
          </>
        }
      >
        <Textarea
          label="Motivo de la reapertura"
          required
          hint="Mínimo 10 caracteres. Quedará registrado en el historial."
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
      </Modal>
    </div>
  )
}
