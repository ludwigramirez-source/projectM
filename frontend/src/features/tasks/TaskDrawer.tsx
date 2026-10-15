import { useRef, useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import {
  CalendarDays,
  CheckSquare,
  ChevronRight,
  Download,
  Flag,
  Paperclip,
  Plus,
  Tag as TagIcon,
  Trash2,
  User,
  X,
} from 'lucide-react'
import { useQueryClient } from '@tanstack/react-query'
import { downloadAttachment, taskKeys, useCreateTag, useTags, useTask, useTaskMutations, useMoveTask, type TaskDetail } from '@/api/tasks'
import { useMilestones, useProject } from '@/api/projects'
import { ApiError, errorMessage } from '@/api/client'
import { Avatar, Badge, Button, Drawer, Input, Modal, PageLoader, Select, StatusBadge, Textarea, useToast } from '@/components/ui'
import { cn } from '@/lib/cn'
import { fmtDateTime } from '@/lib/format'
import { PRIORITIES, TASK_STATUSES, priorityMeta, taskStatusMeta, type TaskStatus, type Tone } from '@/lib/status'

/** Abre el detalle de una tarea desde cualquier pantalla con `?task=<id>`. */
export function useTaskDrawer() {
  const [params, setParams] = useSearchParams()
  return {
    taskId: params.get('task'),
    open: (id: string) => {
      const next = new URLSearchParams(params)
      next.set('task', id)
      setParams(next)
    },
    close: () => {
      const next = new URLSearchParams(params)
      next.delete('task')
      setParams(next)
    },
  }
}

export function TaskDrawer() {
  const { taskId, close } = useTaskDrawer()
  const { data: task, isLoading, error } = useTask(taskId)
  return (
    <Drawer
      open={!!taskId}
      onClose={close}
      title={
        task ? (
          <nav className="flex min-w-0 items-center gap-1 text-xs text-fg-muted">
            <Link to={`/projects/${task.projectId}`} className="truncate hover:text-primary-700">{task.projectName}</Link>
            <ChevronRight className="size-3 shrink-0" />
            <Link to={`/projects/${task.projectId}/tareas?list=${task.listId}`} className="truncate hover:text-primary-700">{task.listName}</Link>
            {task.parentTaskId && (
              <>
                <ChevronRight className="size-3 shrink-0" />
                <ParentLink id={task.parentTaskId} title={task.parentTitle} />
              </>
            )}
          </nav>
        ) : (
          'Tarea'
        )
      }
    >
      {isLoading ? <PageLoader /> : error || !task ? <p className="text-sm text-fg-muted">La tarea no existe o no tienes acceso.</p> : <TaskBody key={task.id} task={task} />}
    </Drawer>
  )
}

function ParentLink({ id, title }: { id: string; title?: string | null }) {
  const { open } = useTaskDrawer()
  return <button onClick={() => open(id)} className="truncate hover:text-primary-700">{title}</button>
}

function TaskBody({ task }: { task: TaskDetail }) {
  const qc = useQueryClient()
  const m = useTaskMutations(task.projectId)
  const move = useMoveTask(task.projectId)
  const { data: project } = useProject(task.projectId)
  const { data: milestones } = useMilestones(task.projectId)
  const toast = useToast()
  const { close } = useTaskDrawer()
  const [title, setTitle] = useState(task.title)
  const [description, setDescription] = useState(task.description ?? '')
  const [blocking, setBlocking] = useState(false)
  const can = task.canEdit

  const onError = (e: unknown) => {
    if (e instanceof ApiError && e.code === 'CONFLICTO_CONCURRENCIA') {
      toast.error('Otra persona modificó la tarea. Se recargaron los datos.')
      qc.invalidateQueries({ queryKey: taskKeys.detail(task.id) })
    } else toast.error(errorMessage(e))
  }

  /** Guarda los campos básicos tomando el último estado conocido de la tarea. */
  const save = (patch: Partial<TaskDetail>) => {
    const latest = qc.getQueryData<TaskDetail>(taskKeys.detail(task.id)) ?? task
    m.update.mutate({ ...latest, ...patch }, { onError })
  }

  const setStatus = (status: TaskStatus) => {
    if (status === 'Bloqueada') return setBlocking(true)
    m.status.mutate({ id: task.id, status }, { onError })
  }

  return (
    <div className="space-y-5">
      <div>
        <textarea
          aria-label="Título"
          className="w-full resize-none rounded-md border border-transparent bg-transparent px-1 text-xl font-semibold hover:border-border focus:border-primary-600 focus:outline-none disabled:hover:border-transparent"
          rows={Math.min(3, Math.ceil(title.length / 50) || 1)}
          value={title}
          disabled={!can}
          onChange={(e) => setTitle(e.target.value)}
          onBlur={() => title.trim() && title !== task.title && save({ title: title.trim() })}
          onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), (e.target as HTMLTextAreaElement).blur())}
        />
        <div className="mt-1 flex flex-wrap items-center gap-2 px-1">
          <StatusBadge status={task.status} overdue={task.isOverdue} />
          {task.status === 'Bloqueada' && task.blockedReason && <span className="text-xs text-danger-700">Motivo: {task.blockedReason}</span>}
          {task.completedAt && <span className="text-xs text-fg-muted">Completada el {fmtDateTime(task.completedAt)}</span>}
        </div>
      </div>

      <dl className="grid grid-cols-1 gap-x-4 gap-y-3 text-sm sm:grid-cols-2">
        <Prop label="Estado" icon={<CheckSquare className="size-4" />}>
          <Select aria-label="Estado" value={task.status} disabled={!can} onChange={(e) => setStatus(e.target.value as TaskStatus)}>
            {TASK_STATUSES.map((s) => <option key={s} value={s}>{taskStatusMeta[s].label}</option>)}
          </Select>
        </Prop>
        <Prop label="Prioridad" icon={<Flag className="size-4" />}>
          <Select aria-label="Prioridad" value={task.priority} disabled={!can} onChange={(e) => save({ priority: e.target.value as TaskDetail['priority'] })}>
            {PRIORITIES.map((p) => <option key={p} value={p}>{priorityMeta[p].label}</option>)}
          </Select>
        </Prop>
        <Prop label="Responsable" icon={<User className="size-4" />}>
          <Select aria-label="Responsable" value={task.assigneeId ?? ''} disabled={!can} onChange={(e) => save({ assigneeId: e.target.value || null })}>
            <option value="">Sin asignar</option>
            {project?.members.map((u) => <option key={u.userId} value={u.userId}>{u.fullName}</option>)}
          </Select>
        </Prop>
        <Prop label="Lista" icon={<ChevronRight className="size-4" />}>
          <Select
            aria-label="Lista"
            value={task.listId}
            disabled={!can || !!task.parentTaskId}
            onChange={(e) => move.mutate({ id: task.id, listId: e.target.value }, { onError })}
          >
            {project?.lists.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </Select>
        </Prop>
        <Prop label="Inicio" icon={<CalendarDays className="size-4" />}>
          <Input aria-label="Inicio" type="date" value={task.startDate ?? ''} disabled={!can} onChange={(e) => save({ startDate: e.target.value || null })} />
        </Prop>
        <Prop label="Vencimiento" icon={<CalendarDays className="size-4" />}>
          <Input
            aria-label="Vencimiento"
            type="date"
            value={task.dueDate ?? ''}
            disabled={!can}
            className={cn(task.isOverdue && 'border-danger-500 text-danger-700')}
            onChange={(e) => save({ dueDate: e.target.value || null })}
          />
        </Prop>
        <Prop label="Hito" icon={<Flag className="size-4" />}>
          <Select aria-label="Hito" value={task.milestoneId ?? ''} disabled={!can} onChange={(e) => save({ milestoneId: e.target.value || null })}>
            <option value="">Sin hito</option>
            {milestones?.map((ms) => <option key={ms.id} value={ms.id}>{ms.name}</option>)}
          </Select>
        </Prop>
        <Prop label="Etiquetas" icon={<TagIcon className="size-4" />}>
          <TagPicker task={task} disabled={!can} />
        </Prop>
      </dl>

      <section>
        <h3 className="mb-1 text-xs font-semibold text-fg-muted uppercase">Descripción</h3>
        <Textarea
          aria-label="Descripción"
          rows={5}
          placeholder={can ? 'Agrega detalles, criterios de aceptación, enlaces…' : 'Sin descripción'}
          value={description}
          disabled={!can}
          onChange={(e) => setDescription(e.target.value)}
          onBlur={() => description !== (task.description ?? '') && save({ description })}
        />
      </section>

      {task.customFields.length > 0 && (
        <section>
          <h3 className="mb-2 text-xs font-semibold text-fg-muted uppercase">Campos personalizados</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            {task.customFields.map((f) => (
              <CustomFieldInput key={f.fieldId} task={task} field={f} disabled={!can} onError={onError} />
            ))}
          </div>
        </section>
      )}

      {!task.parentTaskId && <Subtasks task={task} can={can} onError={onError} />}
      <Attachments task={task} can={can} onError={onError} />

      <footer className="flex flex-wrap items-center justify-between gap-2 border-t border-border pt-3 text-xs text-fg-muted">
        <span>
          Creada por {task.createdByName} · {fmtDateTime(task.createdAt)} · Actualizada {fmtDateTime(task.updatedAt)}
        </span>
        {can && (
          <Button
            size="sm"
            variant="ghost"
            icon={<Trash2 className="size-3.5 text-danger-500" />}
            onClick={() =>
              confirm('¿Eliminar la tarea y sus subtareas?') &&
              m.remove.mutate(task.id, { onSuccess: () => { toast.success('Tarea eliminada'); close() }, onError })
            }
          >
            Eliminar
          </Button>
        )}
      </footer>

      {blocking && (
        <BlockReasonModal
          onCancel={() => setBlocking(false)}
          onConfirm={(reason) =>
            m.status.mutate({ id: task.id, status: 'Bloqueada', blockedReason: reason }, { onSuccess: () => setBlocking(false), onError })
          }
          loading={m.status.isPending}
        />
      )}
    </div>
  )
}

function Prop({ label, icon, children }: { label: string; icon: ReactNode; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <dt className="flex items-center gap-1.5 text-xs text-fg-muted">{icon} {label}</dt>
      <dd>{children}</dd>
    </div>
  )
}

export function BlockReasonModal({ onCancel, onConfirm, loading }: { onCancel: () => void; onConfirm: (reason: string) => void; loading?: boolean }) {
  const [reason, setReason] = useState('')
  return (
    <Modal
      open
      size="sm"
      onClose={onCancel}
      title="Bloquear tarea"
      footer={
        <>
          <Button variant="secondary" onClick={onCancel}>Cancelar</Button>
          <Button variant="danger" disabled={!reason.trim()} loading={loading} onClick={() => onConfirm(reason.trim())}>Bloquear</Button>
        </>
      }
    >
      <Textarea label="Motivo del bloqueo" required autoFocus value={reason} onChange={(e) => setReason(e.target.value)} hint="Será visible en la vista de riesgos y bloqueos." />
    </Modal>
  )
}

function TagPicker({ task, disabled }: { task: TaskDetail; disabled: boolean }) {
  const { data: tags } = useTags()
  const m = useTaskMutations(task.projectId)
  const createTag = useCreateTag()
  const [adding, setAdding] = useState(false)
  const [name, setName] = useState('')
  const ids = task.tags.map((t) => t.id)
  const set = (tagIds: string[]) => m.tags.mutate({ id: task.id, tagIds })
  return (
    <div className="flex flex-wrap items-center gap-1">
      {task.tags.map((t) => (
        <Badge key={t.id} tone={t.color as Tone}>
          {t.name}
          {!disabled && (
            <button aria-label={`Quitar ${t.name}`} onClick={() => set(ids.filter((x) => x !== t.id))}>
              <X className="size-3" />
            </button>
          )}
        </Badge>
      ))}
      {!disabled &&
        (adding ? (
          <div className="flex items-center gap-1">
            <Input
              aria-label="Etiqueta"
              list="all-tags"
              autoFocus
              className="h-7 w-36 text-xs"
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={async (e) => {
                if (e.key === 'Escape') setAdding(false)
                if (e.key !== 'Enter' || !name.trim()) return
                const existing = tags?.find((t) => t.name.toLowerCase() === name.trim().toLowerCase())
                const tag = existing ?? (await createTag.mutateAsync({ name: name.trim(), color: 'secondary' }))
                if (!ids.includes(tag.id)) set([...ids, tag.id])
                setName('')
                setAdding(false)
              }}
              onBlur={() => !name && setAdding(false)}
            />
            <datalist id="all-tags">{tags?.map((t) => <option key={t.id} value={t.name} />)}</datalist>
          </div>
        ) : (
          <button onClick={() => setAdding(true)} className="inline-flex items-center gap-1 rounded-full border border-dashed border-border-strong px-2 py-0.5 text-xs text-fg-muted hover:text-fg">
            <Plus className="size-3" /> Etiqueta
          </button>
        ))}
    </div>
  )
}

function CustomFieldInput({ task, field, disabled, onError }: { task: TaskDetail; field: TaskDetail['customFields'][number]; disabled: boolean; onError: (e: unknown) => void }) {
  const m = useTaskMutations(task.projectId)
  const [value, setValue] = useState(field.value ?? '')
  const commit = (v: string) => v !== (field.value ?? '') && m.field.mutate({ id: task.id, fieldId: field.fieldId, value: v || null }, { onError: (e) => { setValue(field.value ?? ''); onError(e) } })
  const label = field.name + (field.isRequired ? ' *' : '')
  if (field.type === 'Seleccion')
    return (
      <Select label={label} value={value} disabled={disabled} onChange={(e) => { setValue(e.target.value); commit(e.target.value) }}>
        <option value="">—</option>
        {field.options.map((o) => <option key={o}>{o}</option>)}
      </Select>
    )
  return (
    <Input
      label={label}
      type={field.type === 'Numero' ? 'number' : field.type === 'Fecha' ? 'date' : 'text'}
      value={value}
      disabled={disabled}
      onChange={(e) => {
        setValue(e.target.value)
        if (field.type === 'Fecha') commit(e.target.value)
      }}
      onBlur={() => field.type !== 'Fecha' && commit(value)}
    />
  )
}

function Subtasks({ task, can, onError }: { task: TaskDetail; can: boolean; onError: (e: unknown) => void }) {
  const m = useTaskMutations(task.projectId)
  const { open } = useTaskDrawer()
  const [title, setTitle] = useState('')
  const done = task.subtasks.filter((s) => s.status === 'Completada').length
  return (
    <section>
      <h3 className="mb-2 flex items-center justify-between text-xs font-semibold text-fg-muted uppercase">
        Subtareas
        {task.subtasks.length > 0 && <span className="font-normal normal-case">{done}/{task.subtasks.length}</span>}
      </h3>
      <ul className="divide-y divide-border rounded-md border border-border">
        {task.subtasks.map((s) => (
          <li key={s.id} className="flex items-center gap-2 px-3 py-2">
            <input
              type="checkbox"
              className="size-4 accent-success-700"
              aria-label={`Completar ${s.title}`}
              checked={s.status === 'Completada'}
              disabled={!can}
              onChange={(e) => m.status.mutate({ id: s.id, status: e.target.checked ? 'Completada' : 'Pendiente' }, { onError })}
            />
            <button onClick={() => open(s.id)} className={cn('flex-1 truncate text-left text-sm hover:text-primary-700', s.status === 'Completada' && 'text-fg-muted line-through')}>
              {s.title}
            </button>
            <Avatar name={s.assigneeName} size="sm" />
          </li>
        ))}
        {can && (
          <li className="px-3 py-1.5">
            <input
              aria-label="Nueva subtarea"
              placeholder="+ Agregar subtarea (Enter)"
              className="w-full bg-transparent py-1 text-sm placeholder:text-fg-muted focus:outline-none"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              onKeyDown={(e) => {
                if (e.key !== 'Enter' || !title.trim()) return
                m.create.mutate({ listId: task.listId, parentTaskId: task.id, title: title.trim() }, { onSuccess: () => setTitle(''), onError })
              }}
            />
          </li>
        )}
      </ul>
    </section>
  )
}

function Attachments({ task, can, onError }: { task: TaskDetail; can: boolean; onError: (e: unknown) => void }) {
  const m = useTaskMutations(task.projectId)
  const input = useRef<HTMLInputElement>(null)
  const toast = useToast()
  const size = (b: number) => (b < 1024 ? `${b} B` : b < 1048576 ? `${(b / 1024).toFixed(0)} KB` : `${(b / 1048576).toFixed(1)} MB`)
  return (
    <section>
      <h3 className="mb-2 flex items-center justify-between text-xs font-semibold text-fg-muted uppercase">
        Adjuntos ({task.attachments.length})
        {can && (
          <>
            <Button size="sm" variant="ghost" icon={<Paperclip className="size-3.5" />} loading={m.upload.isPending} onClick={() => input.current?.click()}>
              Adjuntar
            </Button>
            <input
              ref={input}
              type="file"
              hidden
              onChange={(e) => {
                const file = e.target.files?.[0]
                e.target.value = ''
                if (file) m.upload.mutate({ id: task.id, file }, { onSuccess: () => toast.success('Archivo adjuntado'), onError })
              }}
            />
          </>
        )}
      </h3>
      {task.attachments.length === 0 ? (
        <p className="text-sm text-fg-muted">Sin archivos. Máx. 20 MB (PDF, Office, imágenes, texto, ZIP).</p>
      ) : (
        <ul className="space-y-1">
          {task.attachments.map((a) => (
            <li key={a.id} className="flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm">
              <Paperclip className="size-4 shrink-0 text-fg-muted" />
              <div className="min-w-0 flex-1">
                <div className="truncate">{a.fileName}</div>
                <div className="text-xs text-fg-muted">{size(a.sizeBytes)} · {a.uploadedByName} · {fmtDateTime(a.createdAt)}</div>
              </div>
              <Button size="sm" variant="ghost" aria-label="Descargar" icon={<Download className="size-4" />} onClick={() => downloadAttachment(a).catch((e) => toast.error(e.message))} />
              {can && (
                <Button size="sm" variant="ghost" aria-label="Eliminar adjunto" icon={<Trash2 className="size-4 text-danger-500" />}
                  onClick={() => confirm(`¿Eliminar ${a.fileName}?`) && m.removeAttachment.mutate({ id: a.id, taskId: task.id }, { onError })} />
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
