import { useState } from 'react'
import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react'
import { useChecklistTemplate, useSaveChecklistTemplate, type ChecklistTemplateItem } from '@/api/projects'
import { errorMessage } from '@/api/client'
import { Button, Card, Checkbox, Input, PageHeader, PageLoader, useToast } from '@/components/ui'

/** Checklist de cierre configurable (aplica a los cierres que se inicien después). */
export function ClosureChecklistPage() {
  const { data } = useChecklistTemplate()
  return data ? <ChecklistEditor key={JSON.stringify(data)} initial={data} /> : <PageLoader />
}

function ChecklistEditor({ initial }: { initial: ChecklistTemplateItem[] }) {
  const save = useSaveChecklistTemplate()
  const toast = useToast()
  const [items, setItems] = useState<ChecklistTemplateItem[]>(initial)

  const move = (i: number, d: -1 | 1) => {
    const next = [...items]
    ;[next[i], next[i + d]] = [next[i + d]!, next[i]!]
    setItems(next)
  }

  return (
    <div>
      <PageHeader
        title="Checklist de cierre"
        subtitle="Ítems que se copian a cada proyecto al iniciar su cierre"
        actions={
          <Button loading={save.isPending} disabled={items.some((i) => !i.text.trim())}
            onClick={() => save.mutate(items, { onSuccess: () => toast.success('Checklist guardado'), onError: (e) => toast.error(errorMessage(e)) })}>
            Guardar
          </Button>
        }
      />
      <div className="mx-auto max-w-3xl p-4 sm:p-6">
        {(
          <Card>
            <ul className="space-y-2">
              {items.map((it, i) => (
                <li key={i} className="flex flex-wrap items-center gap-2 rounded-md border border-border p-2">
                  <Input aria-label="Texto" value={it.text} wrapperClassName="flex-1 min-w-48" onChange={(e) => setItems(items.map((x, idx) => (idx === i ? { ...x, text: e.target.value } : x)))} />
                  <Checkbox label="Obligatorio" checked={it.isRequired} onChange={(e) => setItems(items.map((x, idx) => (idx === i ? { ...x, isRequired: e.target.checked } : x)))} />
                  <Button variant="ghost" size="sm" aria-label="Subir" disabled={i === 0} icon={<ArrowUp className="size-3.5" />} onClick={() => move(i, -1)} />
                  <Button variant="ghost" size="sm" aria-label="Bajar" disabled={i === items.length - 1} icon={<ArrowDown className="size-3.5" />} onClick={() => move(i, 1)} />
                  <Button variant="ghost" size="sm" aria-label="Quitar" icon={<Trash2 className="size-3.5 text-danger-fg" />} onClick={() => setItems(items.filter((_, idx) => idx !== i))} />
                </li>
              ))}
            </ul>
            <Button className="mt-3" variant="secondary" size="sm" icon={<Plus className="size-3.5" />} onClick={() => setItems([...items, { text: '', isRequired: true }])}>
              Agregar ítem
            </Button>
          </Card>
        )}
      </div>
    </div>
  )
}
