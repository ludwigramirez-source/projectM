import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Check, ChevronLeft, ChevronRight, FileStack, Users } from 'lucide-react'
import { useClients, useCreateProject, useSpaces, useTemplate, useTemplates } from '@/api/projects'
import { useUserLookup } from '@/api/users'
import { ApiError, errorMessage } from '@/api/client'
import { useAuth } from '@/app/auth'
import { Avatar, Button, Card, Checkbox, Input, PageHeader, Select, Textarea, useToast } from '@/components/ui'
import { cn } from '@/lib/cn'
import { addDays, format, parseISO } from 'date-fns'
import { fmtShortDate, toDateInput } from '@/lib/format'

const schema = z
  .object({
    spaceId: z.string().min(1, 'Selecciona un espacio'),
    name: z.string().trim().min(1, 'El nombre es obligatorio').max(150),
    clientName: z.string().max(150).optional(),
    objective: z.string().max(2000).optional(),
    scope: z.string().max(4000).optional(),
    plannedStart: z.string().min(1, 'Requerida'),
    plannedEnd: z.string().min(1, 'Requerida'),
    budget: z.string().optional(),
    currency: z.string().length(3, 'Código ISO de 3 letras'),
    managerId: z.string().optional(),
  })
  .refine((v) => !v.plannedStart || !v.plannedEnd || v.plannedEnd >= v.plannedStart, {
    path: ['plannedEnd'],
    message: 'No puede ser anterior al inicio',
  })
  .refine((v) => !v.budget || Number(v.budget) >= 0, { path: ['budget'], message: 'Debe ser un número positivo' })
type Form = z.infer<typeof schema>

const STEPS = ['Datos generales', 'Equipo', 'Plantilla y apertura'] as const

/** Asistente de apertura de proyecto: datos → equipo → plantilla. */
export function NewProjectPage() {
  const [params] = useSearchParams()
  const [step, setStep] = useState(0)
  const [members, setMembers] = useState<string[]>([])
  const [templateId, setTemplateId] = useState<string>('')
  const [activateNow, setActivateNow] = useState(true)
  const { user } = useAuth()
  const navigate = useNavigate()
  const toast = useToast()
  const { data: spaces } = useSpaces()
  const { data: clients } = useClients()
  const { data: people } = useUserLookup()
  const { data: templates } = useTemplates()
  const { data: template } = useTemplate(templateId || null)
  const create = useCreateProject()
  const [[today, inTwoMonths]] = useState(() => [toDateInput(new Date()), toDateInput(addDays(new Date(), 60))])

  const {
    register,
    handleSubmit,
    trigger,
    control,
    setError,
    formState: { errors },
  } = useForm<Form>({
    resolver: zodResolver(schema),
    defaultValues: {
      spaceId: params.get('spaceId') ?? '',
      plannedStart: today,
      plannedEnd: inTwoMonths,
      currency: 'USD',
      managerId: user?.id,
    },
  })
  const plannedStart = useWatch({ control, name: 'plannedStart' })
  const managerId = useWatch({ control, name: 'managerId' })

  const [lastStepAt, setLastStepAt] = useState(0)
  const isLast = step === STEPS.length - 1
  const next = async () => {
    if (step === 0 && !(await trigger())) return
    const to = Math.min(step + 1, STEPS.length - 1)
    if (to === STEPS.length - 1) setLastStepAt(Date.now())
    setStep(to)
  }

  const onSubmit = (f: Form) =>
    create.mutate(
      {
        spaceId: f.spaceId,
        name: f.name,
        clientName: f.clientName || null,
        objective: f.objective,
        scope: f.scope,
        plannedStart: f.plannedStart,
        plannedEnd: f.plannedEnd,
        budget: f.budget ? Number(f.budget) : null,
        currency: f.currency.toUpperCase(),
        managerId: user?.role === 'Admin' ? f.managerId || null : null,
        memberIds: members,
        templateId: templateId || null,
        activateNow,
      },
      {
        onSuccess: (p) => {
          toast.success(`Proyecto ${p.code} creado`)
          navigate(`/projects/${p.id}`)
        },
        onError: (e) => {
          if (e instanceof ApiError && e.errors) {
            for (const [k, v] of Object.entries(e.errors)) setError(k as keyof Form, { message: v[0] })
            setStep(0)
          }
          toast.error(errorMessage(e))
        },
      },
    )

  const toggleMember = (id: string) => setMembers((m) => (m.includes(id) ? m.filter((x) => x !== id) : [...m, id]))

  return (
    <div>
      <PageHeader title="Apertura de proyecto" subtitle="Define el proyecto, su equipo y las tareas iniciales" />
      <div className="mx-auto max-w-3xl space-y-4 p-4 sm:p-6">
        <ol className="flex items-center gap-2 text-sm">
          {STEPS.map((s, i) => (
            <li key={s} className="flex flex-1 items-center gap-2">
              <span
                className={cn(
                  'flex size-6 shrink-0 items-center justify-center rounded-full text-xs font-semibold',
                  i < step ? 'bg-success-solid text-on-success-solid' : i === step ? 'bg-primary-solid text-on-primary-solid' : 'bg-track text-fg-muted',
                )}
              >
                {i < step ? <Check className="size-3.5" /> : i + 1}
              </span>
              <span className={cn('hidden truncate sm:inline', i === step ? 'font-medium' : 'text-fg-muted')}>{s}</span>
              {i < STEPS.length - 1 && <span className="h-px flex-1 bg-border" />}
            </li>
          ))}
        </ol>

        <form
          noValidate
          onSubmit={(e) => {
            // Enter en un campo o doble clic en "Siguiente" no deben crear el proyecto antes del último paso.
            if (!isLast || Date.now() - lastStepAt < 600) {
              e.preventDefault()
              if (!isLast) void next()
              return
            }
            void handleSubmit(onSubmit)(e)
          }}
        >
          <Card className={cn(step !== 0 && 'hidden')}>
            <div className="grid gap-4 sm:grid-cols-2">
              <Input label="Nombre del proyecto" required error={errors.name?.message} wrapperClassName="sm:col-span-2" {...register('name')} />
              <Select label="Espacio" required error={errors.spaceId?.message} {...register('spaceId')}>
                <option value="">Selecciona…</option>
                {spaces?.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </Select>
              <div>
                <Input label="Cliente" list="clients" placeholder="Escribe o elige" error={errors.clientName?.message} {...register('clientName')} />
                <datalist id="clients">
                  {clients?.map((c) => <option key={c.id} value={c.name} />)}
                </datalist>
              </div>
              <Textarea label="Objetivo" wrapperClassName="sm:col-span-2" error={errors.objective?.message} {...register('objective')} />
              <Textarea label="Alcance" wrapperClassName="sm:col-span-2" error={errors.scope?.message} {...register('scope')} />
              <Input label="Inicio planeado" type="date" required error={errors.plannedStart?.message} {...register('plannedStart')} />
              <Input label="Fin planeado" type="date" required error={errors.plannedEnd?.message} {...register('plannedEnd')} />
              <Input label="Presupuesto (opcional)" type="number" min={0} step="0.01" error={errors.budget?.message} {...register('budget')} />
              <Input label="Moneda" maxLength={3} error={errors.currency?.message} {...register('currency')} />
              {user?.role === 'Admin' && (
                <Select label="Gestor responsable" {...register('managerId')} wrapperClassName="sm:col-span-2" hint="Debe tener rol Gestor o Admin.">
                  {people?.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.fullName}
                    </option>
                  ))}
                </Select>
              )}
            </div>
          </Card>

          <Card className={cn(step !== 1 && 'hidden')} title={<span className="flex items-center gap-2"><Users className="size-4" /> Equipo ({members.length})</span>}>
            <p className="mb-3 text-sm text-fg-muted">El gestor responsable se agrega automáticamente.</p>
            <div className="grid max-h-96 gap-1 overflow-y-auto sm:grid-cols-2">
              {people
                ?.filter((p) => p.id !== (managerId || user?.id))
                .map((p) => (
                  <label key={p.id} className={cn('flex cursor-pointer items-center gap-2 rounded-md border p-2', members.includes(p.id) ? 'border-primary-mark bg-primary-subtle' : 'border-border hover:bg-surface-muted')}>
                    <input type="checkbox" className="size-4 accent-primary-solid" checked={members.includes(p.id)} onChange={() => toggleMember(p.id)} />
                    <Avatar name={p.fullName} size="sm" />
                    <span className="min-w-0">
                      <span className="block truncate text-sm">{p.fullName}</span>
                      <span className="block truncate text-xs text-fg-muted">{p.email}</span>
                    </span>
                  </label>
                ))}
            </div>
          </Card>

          <Card className={cn(step !== 2 && 'hidden')} title={<span className="flex items-center gap-2"><FileStack className="size-4" /> Plantilla de tareas inicial</span>}>
            <div className="space-y-3">
              <Select label="Plantilla" value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
                <option value="">Sin plantilla (lista "General" vacía)</option>
                {templates?.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name} — {t.taskCount} tareas
                  </option>
                ))}
              </Select>
              {template && (
                <div className="max-h-72 overflow-y-auto rounded-md border border-border">
                  <table className="w-full text-sm">
                    <thead className="bg-surface-muted text-left text-xs text-fg-muted">
                      <tr>
                        <th className="px-3 py-1.5 font-medium">Lista</th>
                        <th className="px-3 py-1.5 font-medium">Tarea</th>
                        <th className="px-3 py-1.5 font-medium">Fechas</th>
                      </tr>
                    </thead>
                    <tbody>
                      {template.tasks.map((t, i) => {
                        const start = addDays(parseISO(plannedStart), t.startOffsetDays)
                        return (
                          <tr key={i} className="border-t border-border">
                            <td className="px-3 py-1.5 text-fg-muted">{t.listName}</td>
                            <td className="px-3 py-1.5">{t.title}</td>
                            <td className="px-3 py-1.5 whitespace-nowrap text-fg-muted">
                              {fmtShortDate(start)} – {fmtShortDate(format(addDays(start, t.durationDays - 1), 'yyyy-MM-dd'))}
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>
              )}
              <Checkbox
                label="Activar el proyecto al crearlo (Planeación → Activo)"
                checked={activateNow}
                onChange={(e) => setActivateNow(e.target.checked)}
              />
            </div>
          </Card>

          <div className="mt-4 flex justify-between">
            <Button variant="secondary" onClick={() => (step === 0 ? navigate(-1) : setStep(step - 1))} icon={<ChevronLeft className="size-4" />}>
              {step === 0 ? 'Cancelar' : 'Atrás'}
            </Button>
            {!isLast ? (
              <Button key="next" onClick={next}>
                Siguiente <ChevronRight className="size-4" />
              </Button>
            ) : (
              <Button key="submit" type="submit" variant="success" loading={create.isPending} icon={<Check className="size-4" />}>
                Abrir proyecto
              </Button>
            )}
          </div>
        </form>
      </div>
    </div>
  )
}
