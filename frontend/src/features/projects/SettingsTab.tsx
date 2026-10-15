import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useSpaces, useUpdateProject, type ProjectDetail } from '@/api/projects'
import { useUserLookup } from '@/api/users'
import { ApiError, errorMessage } from '@/api/client'
import { useAuth } from '@/app/auth'
import { Button, Card, Input, Select, Textarea, useToast } from '@/components/ui'
import { healthMeta, type ProjectHealth } from '@/lib/status'

const schema = z
  .object({
    name: z.string().trim().min(1, 'El nombre es obligatorio').max(150),
    spaceId: z.string().min(1),
    clientName: z.string().max(150).optional(),
    objective: z.string().max(2000).optional(),
    scope: z.string().max(4000).optional(),
    plannedStart: z.string().min(1),
    plannedEnd: z.string().min(1),
    budget: z.string().optional(),
    currency: z.string().length(3, 'Código ISO de 3 letras'),
    managerId: z.string().min(1),
    health: z.enum(['EnTiempo', 'EnRiesgo', 'Retrasado']),
  })
  .refine((v) => v.plannedEnd >= v.plannedStart, { path: ['plannedEnd'], message: 'No puede ser anterior al inicio' })
type Form = z.infer<typeof schema>

export function SettingsTab({ project }: { project: ProjectDetail }) {
  const update = useUpdateProject(project.id)
  const { data: spaces } = useSpaces()
  const { data: people } = useUserLookup()
  const { hasRole } = useAuth()
  const toast = useToast()
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isDirty },
    reset,
  } = useForm<Form>({
    resolver: zodResolver(schema),
    values: {
      name: project.name,
      spaceId: project.spaceId,
      clientName: project.clientName ?? '',
      objective: project.objective ?? '',
      scope: project.scope ?? '',
      plannedStart: project.plannedStart,
      plannedEnd: project.plannedEnd,
      budget: project.budget?.toString() ?? '',
      currency: project.currency,
      managerId: project.managerId,
      health: project.health,
    },
  })

  const onSubmit = (f: Form) =>
    update.mutate(
      { ...f, clientName: f.clientName || null, budget: f.budget ? Number(f.budget) : null, rowVersion: project.rowVersion },
      {
        onSuccess: (p) => {
          toast.success('Proyecto actualizado')
          reset(undefined, { keepValues: true })
          void p
        },
        onError: (e) => {
          if (e instanceof ApiError && e.errors) for (const [k, v] of Object.entries(e.errors)) setError(k as keyof Form, { message: v[0] })
          toast.error(e instanceof ApiError && e.code === 'CONFLICTO_CONCURRENCIA' ? 'Otra persona modificó el proyecto. Recarga la página.' : errorMessage(e))
        },
      },
    )

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="mx-auto max-w-3xl p-4 sm:p-6" noValidate>
      <Card title="Datos del proyecto" actions={<Button type="submit" size="sm" disabled={!isDirty} loading={update.isPending}>Guardar cambios</Button>}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Nombre" required wrapperClassName="sm:col-span-2" error={errors.name?.message} {...register('name')} />
          <Select label="Espacio" {...register('spaceId')}>
            {spaces?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </Select>
          <Input label="Cliente" error={errors.clientName?.message} {...register('clientName')} />
          <Textarea label="Objetivo" wrapperClassName="sm:col-span-2" {...register('objective')} />
          <Textarea label="Alcance" wrapperClassName="sm:col-span-2" {...register('scope')} />
          <Input label="Inicio planeado" type="date" error={errors.plannedStart?.message} {...register('plannedStart')} />
          <Input label="Fin planeado" type="date" error={errors.plannedEnd?.message} {...register('plannedEnd')} />
          <Input label="Presupuesto" type="number" min={0} step="0.01" {...register('budget')} />
          <Input label="Moneda" maxLength={3} error={errors.currency?.message} {...register('currency')} />
          <Select label="Salud del proyecto" {...register('health')}>
            {(Object.keys(healthMeta) as ProjectHealth[]).map((h) => <option key={h} value={h}>{healthMeta[h].label}</option>)}
          </Select>
          <Select label="Gestor responsable" disabled={!hasRole('Admin')} hint={!hasRole('Admin') ? 'Solo un Admin puede cambiarlo.' : undefined} {...register('managerId')}>
            {people?.map((p) => <option key={p.id} value={p.id}>{p.fullName}</option>)}
          </Select>
        </div>
      </Card>
    </form>
  )
}
