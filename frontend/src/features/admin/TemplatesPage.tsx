import { useState } from 'react'
import { FileStack, Pencil, Plus, Trash2 } from 'lucide-react'
import { useTemplate, useTemplateMutations, useTemplates, type Template, type TemplateTask } from '@/api/projects'
import { errorMessage } from '@/api/client'
import { Button, Card, EmptyState, Input, Modal, PageHeader, PageLoader, Select, Textarea, useToast } from '@/components/ui'
import { PRIORITIES, priorityMeta, type Priority } from '@/lib/status'

export function TemplatesPage() {
  const { data, isLoading } = useTemplates()
  const { remove } = useTemplateMutations()
  const [editing, setEditing] = useState<string | 'new' | null>(null)
  const toast = useToast()
  return (
    <div>
      <PageHeader
        title="Plantillas de proyecto"
        subtitle="Listas y tareas iniciales que se crean al abrir un proyecto"
        actions={<Button icon={<Plus className="size-4" />} onClick={() => setEditing('new')}>Nueva plantilla</Button>}
      />
      <div className="p-4 sm:p-6">
        {isLoading ? (
          <PageLoader />
        ) : !data?.length ? (
          <Card><EmptyState icon={<FileStack className="size-8" />} title="Sin plantillas" /></Card>
        ) : (
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {data.map((t) => (
              <Card key={t.id} title={t.name} actions={
                <div className="flex gap-1">
                  <Button variant="ghost" size="sm" aria-label="Editar" icon={<Pencil className="size-3.5" />} onClick={() => setEditing(t.id)} />
                  <Button variant="ghost" size="sm" aria-label="Eliminar" icon={<Trash2 className="size-3.5 text-danger-500" />}
                    onClick={() => confirm(`¿Eliminar la plantilla «${t.name}»?`) && remove.mutate(t.id, { onError: (e) => toast.error(errorMessage(e)) })} />
                </div>
              }>
                <p className="text-sm text-fg-muted">{t.description || 'Sin descripción'}</p>
                <p className="mt-2 text-xs">{t.listCount} listas · {t.taskCount} tareas</p>
              </Card>
            ))}
          </div>
        )}
      </div>
      {editing && <TemplateEditor id={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}

const emptyTask = (listName = 'General'): TemplateTask => ({ listName, title: '', description: '', priority: 'Normal', startOffsetDays: 0, durationDays: 1 })

function TemplateEditor({ id, onClose }: { id: string | null; onClose: () => void }) {
  const { data } = useTemplate(id)
  if (id && !data)
    return (
      <Modal open size="lg" onClose={onClose} title="Editar plantilla">
        <PageLoader />
      </Modal>
    )
  return <TemplateForm id={id} initial={data} onClose={onClose} />
}

function TemplateForm({ id, initial, onClose }: { id: string | null; initial?: Template; onClose: () => void }) {
  const { save } = useTemplateMutations()
  const toast = useToast()
  const [name, setName] = useState(initial?.name ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [tasks, setTasks] = useState<TemplateTask[]>(initial?.tasks ?? [emptyTask('Inicio')])

  const set = (i: number, patch: Partial<TemplateTask>) => setTasks(tasks.map((t, idx) => (idx === i ? { ...t, ...patch } : t)))
  const valid = name.trim() && tasks.every((t) => t.title.trim() && t.listName.trim())

  return (
    <Modal
      open
      size="lg"
      onClose={onClose}
      title={id ? 'Editar plantilla' : 'Nueva plantilla'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button disabled={!valid} loading={save.isPending}
            onClick={() => save.mutate({ id: id ?? undefined, name, description, tasks }, { onSuccess: () => { toast.success('Plantilla guardada'); onClose() }, onError: (e) => toast.error(errorMessage(e)) })}>
            Guardar
          </Button>
        </>
      }
    >
      {(
        <div className="space-y-3">
          <Input label="Nombre" required value={name} onChange={(e) => setName(e.target.value)} />
          <Textarea label="Descripción" value={description} onChange={(e) => setDescription(e.target.value)} />
          <div className="text-xs font-medium">Tareas ({tasks.length})</div>
          <p className="text-xs text-fg-muted">El desfase se cuenta en días desde el inicio planeado del proyecto.</p>
          <div className="max-h-[50vh] space-y-2 overflow-y-auto pr-1">
            {tasks.map((t, i) => (
              <div key={i} className="grid grid-cols-2 gap-2 rounded-md border border-border p-2 sm:grid-cols-12">
                <Input aria-label="Lista" placeholder="Lista" value={t.listName} onChange={(e) => set(i, { listName: e.target.value })} wrapperClassName="sm:col-span-2" />
                <Input aria-label="Título" placeholder="Título de la tarea" value={t.title} onChange={(e) => set(i, { title: e.target.value })} wrapperClassName="col-span-2 sm:col-span-4" />
                <Select aria-label="Prioridad" value={t.priority} onChange={(e) => set(i, { priority: e.target.value as Priority })} wrapperClassName="sm:col-span-2">
                  {PRIORITIES.map((p) => <option key={p} value={p}>{priorityMeta[p].label}</option>)}
                </Select>
                <Input aria-label="Desfase (días)" title="Desfase (días)" type="number" min={0} value={t.startOffsetDays} onChange={(e) => set(i, { startOffsetDays: Number(e.target.value) })} wrapperClassName="sm:col-span-1" />
                <Input aria-label="Duración (días)" title="Duración (días)" type="number" min={1} value={t.durationDays} onChange={(e) => set(i, { durationDays: Number(e.target.value) })} wrapperClassName="sm:col-span-2" />
                <Button variant="ghost" aria-label="Quitar tarea" icon={<Trash2 className="size-4 text-danger-500" />} onClick={() => setTasks(tasks.filter((_, idx) => idx !== i))} className="sm:col-span-1" />
              </div>
            ))}
          </div>
          <Button variant="secondary" size="sm" icon={<Plus className="size-3.5" />} onClick={() => setTasks([...tasks, emptyTask(tasks.at(-1)?.listName)])}>
            Agregar tarea
          </Button>
        </div>
      )}
    </Modal>
  )
}
