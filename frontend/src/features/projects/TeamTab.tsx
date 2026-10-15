import { useState } from 'react'
import { Crown, UserMinus, UserPlus } from 'lucide-react'
import { useMemberMutations, type ProjectDetail } from '@/api/projects'
import { useUserLookup } from '@/api/users'
import { errorMessage } from '@/api/client'
import { roleLabel } from '@/app/auth'
import { Avatar, Badge, Button, Card, Select, useToast } from '@/components/ui'

export function TeamTab({ project }: { project: ProjectDetail }) {
  const { data: people } = useUserLookup()
  const { add, remove } = useMemberMutations(project.id)
  const [selected, setSelected] = useState('')
  const toast = useToast()
  const can = project.permissions.canManage
  const candidates = people?.filter((p) => !project.members.some((m) => m.userId === p.id)) ?? []

  return (
    <div className="mx-auto max-w-3xl space-y-4 p-4 sm:p-6">
      {can && (
        <Card title="Agregar al equipo">
          <div className="flex flex-wrap gap-2">
            <Select value={selected} onChange={(e) => setSelected(e.target.value)} wrapperClassName="flex-1 min-w-56" aria-label="Persona">
              <option value="">Selecciona una persona…</option>
              {candidates.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.fullName} — {p.email}
                </option>
              ))}
            </Select>
            <Button
              icon={<UserPlus className="size-4" />}
              disabled={!selected}
              loading={add.isPending}
              onClick={() =>
                add.mutate(selected, {
                  onSuccess: () => {
                    setSelected('')
                    toast.success('Miembro agregado')
                  },
                  onError: (e) => toast.error(errorMessage(e)),
                })
              }
            >
              Agregar
            </Button>
          </div>
        </Card>
      )}
      <Card title={`Equipo (${project.members.length})`} bodyClassName="p-0">
        <ul className="divide-y divide-border">
          {project.members.map((m) => (
            <li key={m.userId} className="flex items-center gap-3 px-4 py-2.5">
              <Avatar name={m.fullName} />
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2 text-sm font-medium">
                  {m.fullName}
                  {m.isManager && (
                    <Badge tone="accent">
                      <Crown className="size-3" /> Gestor responsable
                    </Badge>
                  )}
                </div>
                <div className="truncate text-xs text-fg-muted">
                  {m.email} · {roleLabel[m.role]}
                </div>
              </div>
              {can && !m.isManager && (
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={`Quitar a ${m.fullName}`}
                  icon={<UserMinus className="size-4 text-danger-500" />}
                  onClick={() => confirm(`¿Quitar a ${m.fullName} del proyecto?`) && remove.mutate(m.userId, { onError: (e) => toast.error(errorMessage(e)) })}
                />
              )}
            </li>
          ))}
        </ul>
      </Card>
    </div>
  )
}
