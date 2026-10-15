import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { KeyRound, Pencil, Plus, Search, UserX, Users } from 'lucide-react'
import {
  Avatar,
  Badge,
  Button,
  Card,
  Checkbox,
  EmptyState,
  Input,
  Modal,
  PageHeader,
  PageLoader,
  Pagination,
  Select,
  useToast,
} from '@/components/ui'
import { useDeactivateUser, useResetPassword, useSaveUser, useUsers, type UserDto } from '@/api/users'
import { ApiError, errorMessage, type Role } from '@/api/client'
import { roleLabel, useAuth } from '@/app/auth'
import { fmtDate } from '@/lib/format'
import type { Tone } from '@/lib/status'

const ROLES: Role[] = ['Admin', 'Gestor', 'Miembro', 'Observador']
const roleTone: Record<Role, Tone> = { Admin: 'danger', Gestor: 'primary', Miembro: 'secondary', Observador: 'neutral' }

const password = z
  .string()
  .min(10, 'Mínimo 10 caracteres')
  .regex(/[a-z]/, 'Incluye una minúscula')
  .regex(/[A-Z]/, 'Incluye una mayúscula')
  .regex(/\d/, 'Incluye un número')

export function UsersPage() {
  const [q, setQ] = useState('')
  const [role, setRole] = useState('')
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<UserDto | 'new' | null>(null)
  const [resetting, setResetting] = useState<UserDto | null>(null)
  const { data, isLoading } = useUsers({ q, role, page })
  const deactivate = useDeactivateUser()
  const toast = useToast()
  const { user: me } = useAuth()

  return (
    <div>
      <PageHeader
        title="Usuarios"
        subtitle="Gestiona las cuentas y roles del equipo"
        actions={
          <Button icon={<Plus className="size-4" />} onClick={() => setEditing('new')}>
            Nuevo usuario
          </Button>
        }
      />
      <div className="space-y-4 p-4 sm:p-6">
        <div className="flex flex-wrap gap-2">
          <div className="relative min-w-56 flex-1">
            <Search className="pointer-events-none absolute top-2.5 left-2.5 size-4 text-fg-muted" />
            <Input
              placeholder="Buscar por nombre o correo"
              className="pl-8"
              value={q}
              onChange={(e) => {
                setQ(e.target.value)
                setPage(1)
              }}
              aria-label="Buscar usuarios"
            />
          </div>
          <Select
            value={role}
            onChange={(e) => {
              setRole(e.target.value)
              setPage(1)
            }}
            aria-label="Filtrar por rol"
            wrapperClassName="w-44"
          >
            <option value="">Todos los roles</option>
            {ROLES.map((r) => (
              <option key={r} value={r}>
                {roleLabel[r]}
              </option>
            ))}
          </Select>
        </div>
        <Card bodyClassName="p-0">
          {isLoading ? (
            <PageLoader />
          ) : !data?.items.length ? (
            <EmptyState icon={<Users className="size-8" />} title="No hay usuarios que coincidan" />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="border-b border-border bg-surface-muted text-left text-xs text-fg-muted">
                  <tr>
                    <th className="px-4 py-2 font-medium">Nombre</th>
                    <th className="px-4 py-2 font-medium">Rol</th>
                    <th className="px-4 py-2 font-medium">Estado</th>
                    <th className="hidden px-4 py-2 font-medium md:table-cell">Alta</th>
                    <th className="px-4 py-2" />
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((u) => (
                    <tr key={u.id} className="border-b border-border last:border-0 hover:bg-surface-muted/60">
                      <td className="px-4 py-2.5">
                        <div className="flex items-center gap-2.5">
                          <Avatar name={u.fullName} />
                          <div className="min-w-0">
                            <div className="font-medium">{u.fullName}</div>
                            <div className="truncate text-xs text-fg-muted">{u.email}</div>
                          </div>
                        </div>
                      </td>
                      <td className="px-4 py-2.5">
                        <Badge tone={roleTone[u.role]}>{roleLabel[u.role]}</Badge>
                      </td>
                      <td className="px-4 py-2.5">
                        {u.isActive ? <Badge tone="success" dot>Activo</Badge> : <Badge tone="neutral">Inactivo</Badge>}
                      </td>
                      <td className="hidden px-4 py-2.5 text-fg-muted md:table-cell">{fmtDate(u.createdAt)}</td>
                      <td className="px-4 py-2.5">
                        <div className="flex justify-end gap-1">
                          <Button variant="ghost" size="sm" aria-label="Editar" icon={<Pencil className="size-4" />} onClick={() => setEditing(u)} />
                          <Button variant="ghost" size="sm" aria-label="Restablecer contraseña" icon={<KeyRound className="size-4" />} onClick={() => setResetting(u)} />
                          {u.isActive && u.id !== me?.id && (
                            <Button
                              variant="ghost"
                              size="sm"
                              aria-label="Desactivar"
                              icon={<UserX className="size-4 text-danger-500" />}
                              onClick={() => {
                                if (!confirm(`¿Desactivar a ${u.fullName}? Se cerrarán sus sesiones.`)) return
                                deactivate.mutate(u.id, {
                                  onSuccess: () => toast.success('Usuario desactivado'),
                                  onError: (e) => toast.error(errorMessage(e)),
                                })
                              }}
                            />
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
        {data && <Pagination page={data.page} pageSize={data.pageSize} total={data.totalCount} onChange={setPage} />}
      </div>
      {editing && <UserFormModal user={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {resetting && <ResetPasswordModal user={resetting} onClose={() => setResetting(null)} />}
    </div>
  )
}

const userSchema = z.object({
  fullName: z.string().trim().min(1, 'El nombre es obligatorio').max(150),
  email: z.email('Correo no válido'),
  role: z.enum(['Admin', 'Gestor', 'Miembro', 'Observador']),
  isActive: z.boolean(),
  password: z.string().optional(),
})
type UserForm = z.infer<typeof userSchema>

function UserFormModal({ user, onClose }: { user: UserDto | null; onClose: () => void }) {
  const save = useSaveUser()
  const toast = useToast()
  const schema = user ? userSchema : userSchema.extend({ password })
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<UserForm>({
    resolver: zodResolver(schema),
    defaultValues: user
      ? { fullName: user.fullName, email: user.email, role: user.role, isActive: user.isActive }
      : { role: 'Miembro', isActive: true },
  })

  const onSubmit = (f: UserForm) =>
    save.mutate(
      { ...f, id: user?.id },
      {
        onSuccess: () => {
          toast.success(user ? 'Usuario actualizado' : 'Usuario creado')
          onClose()
        },
        onError: (e) => {
          if (e instanceof ApiError && e.errors) {
            for (const [k, v] of Object.entries(e.errors)) setError(k as keyof UserForm, { message: v[0] })
          } else toast.error(errorMessage(e))
        },
      },
    )

  return (
    <Modal
      open
      onClose={onClose}
      title={user ? 'Editar usuario' : 'Nuevo usuario'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button onClick={handleSubmit(onSubmit)} loading={save.isPending}>
            Guardar
          </Button>
        </>
      }
    >
      <form className="space-y-3" onSubmit={handleSubmit(onSubmit)}>
        <Input label="Nombre completo" required error={errors.fullName?.message} {...register('fullName')} />
        <Input label="Correo" type="email" required error={errors.email?.message} {...register('email')} />
        <Select label="Rol" required error={errors.role?.message} {...register('role')}>
          {ROLES.map((r) => (
            <option key={r} value={r}>
              {roleLabel[r]}
            </option>
          ))}
        </Select>
        {!user && (
          <Input
            label="Contraseña inicial"
            type="password"
            autoComplete="new-password"
            required
            hint="Mínimo 10 caracteres con mayúscula, minúscula y número."
            error={errors.password?.message}
            {...register('password')}
          />
        )}
        {user && <Checkbox label="Usuario activo" {...register('isActive')} />}
        <button type="submit" hidden />
      </form>
    </Modal>
  )
}

function ResetPasswordModal({ user, onClose }: { user: UserDto; onClose: () => void }) {
  const reset = useResetPassword()
  const toast = useToast()
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<{ newPassword: string }>({ resolver: zodResolver(z.object({ newPassword: password })) })
  const onSubmit = (f: { newPassword: string }) =>
    reset.mutate(
      { id: user.id, newPassword: f.newPassword },
      {
        onSuccess: () => {
          toast.success('Contraseña restablecida. Se cerraron sus sesiones.')
          onClose()
        },
        onError: (e) => toast.error(errorMessage(e)),
      },
    )
  return (
    <Modal
      open
      size="sm"
      onClose={onClose}
      title="Restablecer contraseña"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button onClick={handleSubmit(onSubmit)} loading={reset.isPending}>
            Restablecer
          </Button>
        </>
      }
    >
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-3">
        <p className="text-sm text-fg-muted">
          Nueva contraseña para <strong className="text-fg">{user.fullName}</strong>.
        </p>
        <Input label="Nueva contraseña" type="password" autoComplete="new-password" error={errors.newPassword?.message} {...register('newPassword')} />
        <button type="submit" hidden />
      </form>
    </Modal>
  )
}
