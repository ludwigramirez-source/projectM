import { useState } from 'react'
import { Link, NavLink, useMatch } from 'react-router-dom'
import { ChevronRight, FolderKanban, List, Plus, Lock } from 'lucide-react'
import { useTree, useSaveSpace } from '@/api/projects'
import { useAuth } from '@/app/auth'
import { cn } from '@/lib/cn'
import { toneDot, type Tone } from '@/lib/status'
import { Button, Input, Modal, Select, Textarea, useToast } from '@/components/ui'
import { errorMessage } from '@/api/client'
import { NavSection } from './Nav'

const SPACE_COLORS: { value: Tone; label: string }[] = [
  { value: 'primary', label: 'Azul' },
  { value: 'secondary', label: 'Pizarra' },
  { value: 'accent', label: 'Menta' },
  { value: 'success', label: 'Verde' },
  { value: 'danger', label: 'Rojo' },
]

/** Árbol Espacio > Proyecto > Lista de la barra lateral. */
export function SpaceTree() {
  const { data } = useTree()
  const { hasRole } = useAuth()
  const projectId = useMatch('/projects/:projectId/*')?.params.projectId
  const [open, setOpen] = useState<Record<string, boolean>>({})
  const [creating, setCreating] = useState(false)
  const canCreate = hasRole('Admin', 'Gestor')
  const isOpen = (id: string, fallback: boolean) => open[id] ?? fallback
  const toggle = (id: string, fallback: boolean) => setOpen((s) => ({ ...s, [id]: !(s[id] ?? fallback) }))

  return (
    <NavSection
      title="Espacios"
      action={
        canCreate && (
          <button onClick={() => setCreating(true)} className="rounded p-0.5 hover:bg-sidebar-active" aria-label="Nuevo espacio" title="Nuevo espacio">
            <Plus className="size-3.5" />
          </button>
        )
      }
    >
      {data?.length === 0 && <p className="px-3 py-1 text-xs text-sidebar-fg-muted">Aún no hay espacios.</p>}
      {data?.map((space) => {
        const spaceOpen = isOpen(space.id, true)
        return (
          <div key={space.id}>
            <button
              onClick={() => toggle(space.id, true)}
              className="flex w-full items-center gap-2 rounded-md px-3 py-1.5 text-left text-sm text-sidebar-fg hover:bg-sidebar-active/60"
              aria-expanded={spaceOpen}
            >
              <ChevronRight className={cn('size-3.5 shrink-0 transition-transform', spaceOpen && 'rotate-90')} />
              <span className={cn('size-2 shrink-0 rounded-sm', toneDot[(space.color as Tone) ?? 'primary'])} />
              <span className="truncate font-medium">{space.name}</span>
            </button>
            {spaceOpen && (
              <div className="ml-4 border-l border-sidebar-active pl-1">
                {space.projects.length === 0 && <p className="px-3 py-1 text-xs text-sidebar-fg-muted">Sin proyectos</p>}
                {space.projects.map((p) => {
                  const projectOpen = isOpen(p.id, p.id === projectId)
                  return (
                    <div key={p.id}>
                      <div className="flex items-center">
                        <button onClick={() => toggle(p.id, p.id === projectId)} className="rounded p-1 text-sidebar-fg-muted hover:text-sidebar-fg" aria-label="Mostrar listas" aria-expanded={projectOpen}>
                          <ChevronRight className={cn('size-3 transition-transform', projectOpen && 'rotate-90')} />
                        </button>
                        <NavLink
                          to={`/projects/${p.id}`}
                          end
                          className={({ isActive }) =>
                            cn(
                              'flex min-w-0 flex-1 items-center gap-2 rounded-md px-1.5 py-1 text-sm',
                              isActive ? 'bg-sidebar-active text-sidebar-fg' : 'text-sidebar-fg-muted hover:text-sidebar-fg',
                            )
                          }
                        >
                          {p.status === 'Cerrado' ? <Lock className="size-3.5 shrink-0" /> : <FolderKanban className="size-3.5 shrink-0" />}
                          <span className="truncate">{p.name}</span>
                        </NavLink>
                      </div>
                      {projectOpen && (
                        <div className="ml-5">
                          {p.lists.map((l) => (
                            <NavLink
                              key={l.id}
                              to={`/projects/${p.id}/lists/${l.id}`}
                              className={({ isActive }) =>
                                cn(
                                  'flex items-center gap-2 rounded-md px-2 py-1 text-xs',
                                  isActive ? 'bg-sidebar-active text-sidebar-fg' : 'text-sidebar-fg-muted hover:text-sidebar-fg',
                                )
                              }
                            >
                              <List className="size-3 shrink-0" />
                              <span className="truncate">{l.name}</span>
                            </NavLink>
                          ))}
                        </div>
                      )}
                    </div>
                  )
                })}
                {canCreate && (
                  <Link to={`/projects/new?spaceId=${space.id}`} className="flex items-center gap-2 px-3 py-1 text-xs text-sidebar-fg-muted hover:text-sidebar-fg">
                    <Plus className="size-3" /> Nuevo proyecto
                  </Link>
                )}
              </div>
            )}
          </div>
        )
      })}
      {creating && <SpaceModal onClose={() => setCreating(false)} />}
    </NavSection>
  )
}

function SpaceModal({ onClose }: { onClose: () => void }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [color, setColor] = useState<Tone>('primary')
  const save = useSaveSpace()
  const toast = useToast()
  const submit = () =>
    save.mutate(
      { name, description, color },
      {
        onSuccess: () => {
          toast.success('Espacio creado')
          onClose()
        },
        onError: (e) => toast.error(errorMessage(e)),
      },
    )
  return (
    <Modal
      open
      onClose={onClose}
      title="Nuevo espacio"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button onClick={submit} loading={save.isPending} disabled={!name.trim()}>
            Crear
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-fg">
        <Input label="Nombre" required value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        <Textarea label="Descripción" value={description} onChange={(e) => setDescription(e.target.value)} />
        <Select label="Color" value={color} onChange={(e) => setColor(e.target.value as Tone)}>
          {SPACE_COLORS.map((c) => (
            <option key={c.value} value={c.value}>
              {c.label}
            </option>
          ))}
        </Select>
      </div>
    </Modal>
  )
}
