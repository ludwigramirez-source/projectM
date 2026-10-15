import { useState } from 'react'
import { Check, Pencil, Plus, Trash2 } from 'lucide-react'
import type { ProjectDetail } from '@/api/projects'
import { useCustomFields, useFieldMutations, useListMutations, type CustomFieldType } from '@/api/tasks'
import { errorMessage } from '@/api/client'
import { Badge, Button, Checkbox, Input, Modal, Select, Tabs, useToast } from '@/components/ui'

const TYPES: { value: CustomFieldType; label: string }[] = [
  { value: 'Texto', label: 'Texto' },
  { value: 'Numero', label: 'Número' },
  { value: 'Fecha', label: 'Fecha' },
  { value: 'Seleccion', label: 'Selección' },
]

/** Gestión de listas y campos personalizados del proyecto (Gestor/Admin). */
export function ListManager({ project, onClose }: { project: ProjectDetail; onClose: () => void }) {
  const [tab, setTab] = useState<'listas' | 'campos'>('listas')
  return (
    <Modal open onClose={onClose} title="Listas y campos personalizados" size="lg">
      <Tabs value={tab} onChange={setTab} items={[{ value: 'listas', label: 'Listas' }, { value: 'campos', label: 'Campos personalizados' }]} />
      <div className="pt-4">{tab === 'listas' ? <Lists project={project} /> : <Fields project={project} />}</div>
    </Modal>
  )
}

function Lists({ project }: { project: ProjectDetail }) {
  const m = useListMutations(project.id)
  const toast = useToast()
  const [name, setName] = useState('')
  const [editing, setEditing] = useState<{ id: string; name: string } | null>(null)
  const onError = (e: unknown) => toast.error(errorMessage(e))
  return (
    <div className="space-y-3">
      <ul className="divide-y divide-border rounded-md border border-border">
        {project.lists.map((l) => (
          <li key={l.id} className="flex items-center gap-2 px-3 py-2 text-sm">
            {editing?.id === l.id ? (
              <>
                <Input aria-label="Nombre de la lista" autoFocus value={editing.name} wrapperClassName="flex-1" onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
                <Button size="sm" icon={<Check className="size-3.5" />} disabled={!editing.name.trim()} onClick={() => m.rename.mutate(editing, { onSuccess: () => setEditing(null), onError })} />
              </>
            ) : (
              <>
                <span className="flex-1">{l.name}</span>
                <span className="text-xs text-fg-muted">{l.taskCount} tareas</span>
                <Button variant="ghost" size="sm" aria-label="Renombrar" icon={<Pencil className="size-3.5" />} onClick={() => setEditing({ id: l.id, name: l.name })} />
                <Button variant="ghost" size="sm" aria-label="Eliminar lista" icon={<Trash2 className="size-3.5 text-danger-500" />}
                  onClick={() => confirm(`¿Eliminar la lista «${l.name}»?`) && m.remove.mutate(l.id, { onError })} />
              </>
            )}
          </li>
        ))}
      </ul>
      <div className="flex gap-2">
        <Input aria-label="Nueva lista" placeholder="Nombre de la nueva lista" value={name} wrapperClassName="flex-1" onChange={(e) => setName(e.target.value)} />
        <Button icon={<Plus className="size-4" />} disabled={!name.trim()} loading={m.create.isPending} onClick={() => m.create.mutate(name.trim(), { onSuccess: () => setName(''), onError })}>
          Agregar
        </Button>
      </div>
    </div>
  )
}

function Fields({ project }: { project: ProjectDetail }) {
  const { data: fields } = useCustomFields(project.id)
  const m = useFieldMutations(project.id)
  const toast = useToast()
  const [name, setName] = useState('')
  const [type, setType] = useState<CustomFieldType>('Texto')
  const [options, setOptions] = useState('')
  const [required, setRequired] = useState(false)
  const onError = (e: unknown) => toast.error(errorMessage(e))
  const parsedOptions = options.split(',').map((o) => o.trim()).filter(Boolean)
  return (
    <div className="space-y-3">
      <ul className="divide-y divide-border rounded-md border border-border">
        {fields?.length === 0 && <li className="px-3 py-2 text-sm text-fg-muted">Sin campos personalizados.</li>}
        {fields?.map((f) => (
          <li key={f.id} className="flex items-center gap-2 px-3 py-2 text-sm">
            <span className="flex-1">
              {f.name} {f.isRequired && <Badge tone="danger">Obligatorio</Badge>}
              {f.type === 'Seleccion' && <span className="block text-xs text-fg-muted">{f.options.join(' · ')}</span>}
            </span>
            <Badge tone="primary">{TYPES.find((t) => t.value === f.type)?.label}</Badge>
            <Button variant="ghost" size="sm" aria-label="Eliminar campo" icon={<Trash2 className="size-3.5 text-danger-500" />}
              onClick={() => confirm(`¿Eliminar el campo «${f.name}» y sus valores?`) && m.remove.mutate(f.id, { onError })} />
          </li>
        ))}
      </ul>
      <div className="grid gap-2 rounded-md border border-border p-3 sm:grid-cols-2">
        <Input label="Nombre" value={name} onChange={(e) => setName(e.target.value)} />
        <Select label="Tipo" value={type} onChange={(e) => setType(e.target.value as CustomFieldType)}>
          {TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
        </Select>
        {type === 'Seleccion' && (
          <Input label="Opciones (separadas por coma)" wrapperClassName="sm:col-span-2" value={options} onChange={(e) => setOptions(e.target.value)} />
        )}
        <Checkbox label="Obligatorio para completar la tarea" checked={required} onChange={(e) => setRequired(e.target.checked)} />
        <div className="flex justify-end sm:col-span-2">
          <Button
            icon={<Plus className="size-4" />}
            disabled={!name.trim() || (type === 'Seleccion' && parsedOptions.length === 0)}
            loading={m.save.isPending}
            onClick={() =>
              m.save.mutate(
                { name: name.trim(), type, options: parsedOptions, isRequired: required },
                { onSuccess: () => { setName(''); setOptions(''); setRequired(false) }, onError },
              )
            }
          >
            Agregar campo
          </Button>
        </div>
      </div>
    </div>
  )
}
