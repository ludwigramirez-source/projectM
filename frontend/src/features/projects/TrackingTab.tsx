import { useState } from 'react'
import { AlertTriangle, CheckCircle2, Flag, OctagonX, Pencil, Plus, ShieldAlert, Trash2 } from 'lucide-react'
import {
  useBlockers,
  useMilestoneMutations,
  useMilestones,
  useRiskMutations,
  useRisks,
  type Milestone,
  type ProjectDetail,
  type Risk,
  type RiskStatus,
  type TaskSummary,
} from '@/api/projects'
import { errorMessage } from '@/api/client'
import { Avatar, Badge, Button, Card, Checkbox, Input, Modal, PageLoader, PriorityBadge, Select, StatusBadge, Textarea, useToast } from '@/components/ui'
import { fmtDate, isOverdue, toDateInput } from '@/lib/format'
import { cn } from '@/lib/cn'
import type { Tone } from '@/lib/status'

const LEVEL = ['', 'Baja', 'Media', 'Alta']
const riskTone = (sev: number): Tone => (sev >= 6 ? 'danger' : sev >= 3 ? 'accent' : 'secondary')
const riskStatusTone: Record<RiskStatus, Tone> = { Abierto: 'danger', Mitigado: 'accent', Cerrado: 'neutral' }

export function TrackingTab({ project }: { project: ProjectDetail }) {
  const { data: blockers } = useBlockers(project.id)
  return (
    <div className="grid gap-4 p-4 sm:p-6 xl:grid-cols-2">
      <Card title={<span className="flex items-center gap-2"><OctagonX className="size-4 text-danger-fg" /> Tareas bloqueadas ({blockers?.blockedTasks.length ?? 0})</span>}>
        <TaskRows tasks={blockers?.blockedTasks} empty="No hay tareas bloqueadas." showReason />
      </Card>
      <Card title={<span className="flex items-center gap-2"><AlertTriangle className="size-4 text-danger-fg" /> Tareas vencidas ({blockers?.overdueTasks.length ?? 0})</span>}>
        <TaskRows tasks={blockers?.overdueTasks} empty="No hay tareas vencidas." />
      </Card>
      <RisksCard project={project} />
      <MilestonesCard project={project} />
    </div>
  )
}

function TaskRows({ tasks, empty, showReason }: { tasks?: TaskSummary[]; empty: string; showReason?: boolean }) {
  if (!tasks) return <PageLoader />
  if (!tasks.length) return <p className="flex items-center gap-2 text-sm text-fg-muted"><CheckCircle2 className="size-4 text-success-fg" /> {empty}</p>
  return (
    <ul className="divide-y divide-border">
      {tasks.map((t) => (
        <li key={t.id} className="flex items-start gap-3 py-2">
          <Avatar name={t.assigneeName} size="sm" />
          <div className="min-w-0 flex-1">
            <div className="truncate text-sm font-medium">{t.title}</div>
            {showReason && t.blockedReason && <div className="text-xs text-danger-fg">Motivo: {t.blockedReason}</div>}
            <div className="mt-1 flex flex-wrap items-center gap-1.5 text-xs text-fg-muted">
              <StatusBadge status={t.status} overdue={isOverdue(t.dueDate, t.status)} />
              <PriorityBadge priority={t.priority} />
              {t.dueDate && <span>Vence {fmtDate(t.dueDate)}</span>}
            </div>
          </div>
        </li>
      ))}
    </ul>
  )
}

function RisksCard({ project }: { project: ProjectDetail }) {
  const { data: risks } = useRisks(project.id)
  const { remove } = useRiskMutations(project.id)
  const [editing, setEditing] = useState<Risk | 'new' | null>(null)
  const toast = useToast()
  const can = project.permissions.canManage
  return (
    <Card
      title={<span className="flex items-center gap-2"><ShieldAlert className="size-4" /> Registro de riesgos</span>}
      actions={can && <Button size="sm" icon={<Plus className="size-3.5" />} onClick={() => setEditing('new')}>Riesgo</Button>}
    >
      {!risks ? (
        <PageLoader />
      ) : risks.length === 0 ? (
        <p className="text-sm text-fg-muted">Sin riesgos registrados.</p>
      ) : (
        <ul className="divide-y divide-border">
          {risks.map((r) => (
            <li key={r.id} className={cn('flex items-start gap-3 py-2', r.status !== 'Abierto' && 'opacity-70')}>
              <span title={`Severidad ${r.severity}/9`}>
                <Badge tone={riskTone(r.severity)}>{r.severity}</Badge>
              </span>
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-medium">{r.title}</span>
                  <Badge tone={riskStatusTone[r.status]}>{r.status}</Badge>
                </div>
                <div className="text-xs text-fg-muted">
                  Prob. {LEVEL[r.probability]} · Impacto {LEVEL[r.impact]}
                  {r.ownerName && ` · Responsable: ${r.ownerName}`}
                </div>
                {r.mitigation && <div className="mt-1 text-xs">Mitigación: {r.mitigation}</div>}
              </div>
              {can && (
                <div className="flex gap-1">
                  <Button variant="ghost" size="sm" aria-label="Editar" icon={<Pencil className="size-3.5" />} onClick={() => setEditing(r)} />
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label="Eliminar"
                    icon={<Trash2 className="size-3.5 text-danger-fg" />}
                    onClick={() => confirm('¿Eliminar el riesgo?') && remove.mutate(r.id, { onError: (e) => toast.error(errorMessage(e)) })}
                  />
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {editing && <RiskModal project={project} risk={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </Card>
  )
}

function RiskModal({ project, risk, onClose }: { project: ProjectDetail; risk: Risk | null; onClose: () => void }) {
  const [f, setF] = useState({
    title: risk?.title ?? '',
    description: risk?.description ?? '',
    probability: risk?.probability ?? 2,
    impact: risk?.impact ?? 2,
    status: risk?.status ?? ('Abierto' as RiskStatus),
    ownerId: risk?.ownerId ?? '',
    mitigation: risk?.mitigation ?? '',
  })
  const { save } = useRiskMutations(project.id)
  const toast = useToast()
  const submit = () =>
    save.mutate(
      { ...f, id: risk?.id, ownerId: f.ownerId || null },
      { onSuccess: () => onClose(), onError: (e) => toast.error(errorMessage(e)) },
    )
  return (
    <Modal
      open
      onClose={onClose}
      title={risk ? 'Editar riesgo' : 'Nuevo riesgo'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button onClick={submit} loading={save.isPending} disabled={!f.title.trim()}>Guardar</Button>
        </>
      }
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <Input label="Título" required wrapperClassName="sm:col-span-2" value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} />
        <Textarea label="Descripción" wrapperClassName="sm:col-span-2" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} />
        <Select label="Probabilidad" value={f.probability} onChange={(e) => setF({ ...f, probability: Number(e.target.value) })}>
          {[1, 2, 3].map((n) => <option key={n} value={n}>{LEVEL[n]}</option>)}
        </Select>
        <Select label="Impacto" value={f.impact} onChange={(e) => setF({ ...f, impact: Number(e.target.value) })}>
          {[1, 2, 3].map((n) => <option key={n} value={n}>{LEVEL[n]}</option>)}
        </Select>
        <Select label="Estado" value={f.status} onChange={(e) => setF({ ...f, status: e.target.value as RiskStatus })}>
          {(['Abierto', 'Mitigado', 'Cerrado'] as RiskStatus[]).map((s) => <option key={s}>{s}</option>)}
        </Select>
        <Select label="Responsable" value={f.ownerId} onChange={(e) => setF({ ...f, ownerId: e.target.value })}>
          <option value="">Sin responsable</option>
          {project.members.map((m) => <option key={m.userId} value={m.userId}>{m.fullName}</option>)}
        </Select>
        <Textarea label="Plan de mitigación" wrapperClassName="sm:col-span-2" value={f.mitigation} onChange={(e) => setF({ ...f, mitigation: e.target.value })} />
      </div>
    </Modal>
  )
}

function MilestonesCard({ project }: { project: ProjectDetail }) {
  const { data } = useMilestones(project.id)
  const { save, remove } = useMilestoneMutations(project.id)
  const [editing, setEditing] = useState<Milestone | 'new' | null>(null)
  const toast = useToast()
  const can = project.permissions.canManage
  const [today] = useState(() => toDateInput(new Date()))
  return (
    <Card
      title={<span className="flex items-center gap-2"><Flag className="size-4" /> Hitos</span>}
      actions={can && <Button size="sm" icon={<Plus className="size-3.5" />} onClick={() => setEditing('new')}>Hito</Button>}
    >
      {!data ? (
        <PageLoader />
      ) : data.length === 0 ? (
        <p className="text-sm text-fg-muted">Sin hitos definidos.</p>
      ) : (
        <ul className="divide-y divide-border">
          {data.map((m) => {
            const late = !m.completedAt && m.dueDate < today
            return (
              <li key={m.id} className="flex items-center gap-3 py-2">
                <input
                  type="checkbox"
                  className="size-4 accent-success-solid"
                  checked={!!m.completedAt}
                  disabled={!can}
                  aria-label="Completado"
                  onChange={(e) =>
                    save.mutate({ id: m.id, name: m.name, description: m.description ?? undefined, dueDate: m.dueDate, completed: e.target.checked }, { onError: (err) => toast.error(errorMessage(err)) })
                  }
                />
                <div className="min-w-0 flex-1">
                  <div className={cn('text-sm font-medium', m.completedAt && 'text-fg-muted line-through')}>{m.name}</div>
                  <div className={cn('text-xs', late ? 'font-medium text-danger-fg' : 'text-fg-muted')}>
                    {fmtDate(m.dueDate)} · {m.completedTasks}/{m.totalTasks} tareas
                  </div>
                </div>
                {can && (
                  <div className="flex gap-1">
                    <Button variant="ghost" size="sm" aria-label="Editar" icon={<Pencil className="size-3.5" />} onClick={() => setEditing(m)} />
                    <Button variant="ghost" size="sm" aria-label="Eliminar" icon={<Trash2 className="size-3.5 text-danger-fg" />} onClick={() => confirm('¿Eliminar el hito?') && remove.mutate(m.id)} />
                  </div>
                )}
              </li>
            )
          })}
        </ul>
      )}
      {editing && <MilestoneModal project={project} milestone={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </Card>
  )
}

function MilestoneModal({ project, milestone, onClose }: { project: ProjectDetail; milestone: Milestone | null; onClose: () => void }) {
  const [name, setName] = useState(milestone?.name ?? '')
  const [description, setDescription] = useState(milestone?.description ?? '')
  const [dueDate, setDueDate] = useState(milestone?.dueDate ?? project.plannedEnd)
  const [completed, setCompleted] = useState(!!milestone?.completedAt)
  const { save } = useMilestoneMutations(project.id)
  const toast = useToast()
  return (
    <Modal
      open
      size="sm"
      onClose={onClose}
      title={milestone ? 'Editar hito' : 'Nuevo hito'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button
            disabled={!name.trim() || !dueDate}
            loading={save.isPending}
            onClick={() => save.mutate({ id: milestone?.id, name, description, dueDate, completed }, { onSuccess: onClose, onError: (e) => toast.error(errorMessage(e)) })}
          >
            Guardar
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Input label="Nombre" required value={name} onChange={(e) => setName(e.target.value)} />
        <Textarea label="Descripción" value={description} onChange={(e) => setDescription(e.target.value)} />
        <Input label="Fecha objetivo" type="date" required value={dueDate} onChange={(e) => setDueDate(e.target.value)} />
        {milestone && <Checkbox label="Completado" checked={completed} onChange={(e) => setCompleted(e.target.checked)} />}
      </div>
    </Modal>
  )
}
