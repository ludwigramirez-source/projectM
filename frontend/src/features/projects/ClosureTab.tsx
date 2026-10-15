import { useState } from 'react'
import { AlertTriangle, CheckCircle2, ClipboardCheck, FileCheck2, Lock, Plus, Printer, Trash2, X } from 'lucide-react'
import { useClosure, useClosureMutations, type Closure, type Lesson, type ProjectDetail } from '@/api/projects'
import { errorMessage } from '@/api/client'
import { Badge, Button, Card, EmptyState, Input, Modal, PageLoader, ProgressBar, Textarea, useToast } from '@/components/ui'
import { cn } from '@/lib/cn'
import { fmtDate, fmtDateTime } from '@/lib/format'
import { differenceInCalendarDays, parseISO } from 'date-fns'

const MIN_JUSTIFICATION = 20

export function ClosureTab({ project }: { project: ProjectDetail }) {
  const { data: closure, isLoading } = useClosure(project.id)
  const m = useClosureMutations(project.id)
  const toast = useToast()
  if (isLoading || !closure) return <PageLoader />

  if (closure.status === 'Planeacion' || closure.status === 'Activo' || closure.status === 'EnPausa')
    return (
      <div className="p-4 sm:p-6">
        <Card>
          <EmptyState
            icon={<ClipboardCheck className="size-8" />}
            title="El proyecto aún no está en cierre"
            description={
              closure.status === 'Planeacion'
                ? 'Activa el proyecto antes de iniciar el cierre.'
                : 'Al iniciar el cierre se crea el checklist de cierre y el borrador del acta. Podrás cancelarlo si es necesario.'
            }
            action={
              project.permissions.canClose &&
              closure.status !== 'Planeacion' && (
                <Button
                  loading={m.start.isPending}
                  onClick={() => m.start.mutate(undefined, { onSuccess: () => toast.success('Cierre iniciado'), onError: (e) => toast.error(errorMessage(e)) })}
                >
                  Iniciar cierre
                </Button>
              )
            }
          />
        </Card>
      </div>
    )

  if (closure.status === 'Cerrado') return <ClosureRecord project={project} closure={closure} />
  // La clave reinicia el formulario del acta cuando el servidor devuelve otro contenido.
  const actaKey = JSON.stringify([closure.summary, closure.deliverables, closure.lessons])
  return <ClosureInProgress key={actaKey} project={project} closure={closure} />
}

function ClosureInProgress({ project, closure }: { project: ProjectDetail; closure: Closure }) {
  const m = useClosureMutations(project.id)
  const toast = useToast()
  const can = project.permissions.canClose
  const [summary, setSummary] = useState(closure.summary ?? '')
  const [deliverables, setDeliverables] = useState<string[]>(closure.deliverables)
  const [newDeliverable, setNewDeliverable] = useState('')
  const [lessons, setLessons] = useState<Lesson[]>(closure.lessons)
  const [closing, setClosing] = useState(false)
  const dirty =
    summary !== (closure.summary ?? '') ||
    JSON.stringify(deliverables) !== JSON.stringify(closure.deliverables) ||
    JSON.stringify(lessons) !== JSON.stringify(closure.lessons)

  const saveActa = () =>
    m.update.mutate(
      { summary, deliverables, lessons },
      { onSuccess: () => toast.success('Acta guardada'), onError: (e) => toast.error(errorMessage(e)) },
    )

  const requiredDone = closure.checklist.filter((i) => i.isRequired && i.isDone).length
  const requiredTotal = closure.checklist.filter((i) => i.isRequired).length

  return (
    <div className="grid gap-4 p-4 sm:p-6 xl:grid-cols-3">
      <div className="space-y-4 xl:col-span-2">
        <Card title={<span className="flex items-center gap-2"><ClipboardCheck className="size-4" /> Checklist de cierre</span>} actions={<span className="text-xs text-fg-muted">{requiredDone}/{requiredTotal} obligatorios</span>}>
          <ul className="space-y-2">
            {closure.checklist.map((i) => (
              <li key={i.id} className={cn('flex items-start gap-3 rounded-md border p-2.5', i.isDone ? 'border-success-200 bg-success-50' : 'border-border')}>
                <input
                  type="checkbox"
                  className="mt-0.5 size-4 accent-success-700"
                  checked={i.isDone}
                  disabled={!can || m.check.isPending}
                  aria-label={i.text}
                  onChange={(e) => m.check.mutate({ itemId: i.id, isDone: e.target.checked, notes: i.notes }, { onError: (err) => toast.error(errorMessage(err)) })}
                />
                <div className="min-w-0 flex-1">
                  <div className="text-sm">
                    {i.text} {i.isRequired ? <Badge tone="danger">Obligatorio</Badge> : <Badge>Opcional</Badge>}
                  </div>
                  {i.isDone && i.doneByName && (
                    <div className="text-xs text-fg-muted">
                      Marcado por {i.doneByName} · {fmtDateTime(i.doneAt)}
                    </div>
                  )}
                </div>
              </li>
            ))}
          </ul>
        </Card>

        <Card title={<span className="flex items-center gap-2"><FileCheck2 className="size-4" /> Acta de cierre</span>} actions={can && <Button size="sm" disabled={!dirty} loading={m.update.isPending} onClick={saveActa}>Guardar acta</Button>}>
          <div className="space-y-4">
            <Textarea label="Resumen del proyecto" required rows={5} value={summary} disabled={!can} onChange={(e) => setSummary(e.target.value)} hint="Resultados, cumplimiento de objetivos y estado final." />
            <div>
              <div className="mb-1 text-xs font-medium">Entregables</div>
              <ul className="space-y-1">
                {deliverables.map((d, idx) => (
                  <li key={idx} className="flex items-center gap-2 rounded-md bg-surface-muted px-2 py-1 text-sm">
                    <CheckCircle2 className="size-4 shrink-0 text-success-700" />
                    <span className="flex-1">{d}</span>
                    {can && (
                      <button onClick={() => setDeliverables(deliverables.filter((_, i) => i !== idx))} aria-label="Quitar entregable" className="text-fg-muted hover:text-danger-500">
                        <X className="size-3.5" />
                      </button>
                    )}
                  </li>
                ))}
              </ul>
              {can && (
                <div className="mt-2 flex gap-2">
                  <Input
                    placeholder="Agregar entregable"
                    value={newDeliverable}
                    wrapperClassName="flex-1"
                    onChange={(e) => setNewDeliverable(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' && newDeliverable.trim()) {
                        e.preventDefault()
                        setDeliverables([...deliverables, newDeliverable.trim()])
                        setNewDeliverable('')
                      }
                    }}
                    aria-label="Nuevo entregable"
                  />
                  <Button variant="secondary" icon={<Plus className="size-4" />} disabled={!newDeliverable.trim()} onClick={() => { setDeliverables([...deliverables, newDeliverable.trim()]); setNewDeliverable('') }}>
                    Agregar
                  </Button>
                </div>
              )}
            </div>
            <div>
              <div className="mb-1 flex items-center justify-between">
                <span className="text-xs font-medium">Lecciones aprendidas</span>
                {can && (
                  <Button size="sm" variant="ghost" icon={<Plus className="size-3.5" />} onClick={() => setLessons([...lessons, { category: 'General', whatWentWell: '', whatToImprove: '', recommendation: '' }])}>
                    Lección
                  </Button>
                )}
              </div>
              {lessons.length === 0 && <p className="text-sm text-fg-muted">Aún no hay lecciones registradas.</p>}
              <div className="space-y-3">
                {lessons.map((l, idx) => {
                  const set = (patch: Partial<Lesson>) => setLessons(lessons.map((x, i) => (i === idx ? { ...x, ...patch } : x)))
                  return (
                    <div key={idx} className="grid gap-2 rounded-md border border-border p-3 sm:grid-cols-2">
                      <div className="flex items-end gap-2 sm:col-span-2">
                        <Input label="Categoría" value={l.category} disabled={!can} wrapperClassName="flex-1" onChange={(e) => set({ category: e.target.value })} />
                        {can && <Button variant="ghost" aria-label="Quitar lección" icon={<Trash2 className="size-4 text-danger-500" />} onClick={() => setLessons(lessons.filter((_, i) => i !== idx))} />}
                      </div>
                      <Textarea label="Qué funcionó bien" value={l.whatWentWell ?? ''} disabled={!can} onChange={(e) => set({ whatWentWell: e.target.value })} />
                      <Textarea label="Qué mejorar" value={l.whatToImprove ?? ''} disabled={!can} onChange={(e) => set({ whatToImprove: e.target.value })} />
                      <Textarea label="Recomendación" wrapperClassName="sm:col-span-2" value={l.recommendation ?? ''} disabled={!can} onChange={(e) => set({ recommendation: e.target.value })} />
                    </div>
                  )
                })}
              </div>
            </div>
          </div>
        </Card>
      </div>

      <div className="space-y-4">
        <MetricsCard closure={closure} />
        <Card title="Validación">
          {closure.problems.length === 0 ? (
            <p className="flex items-center gap-2 text-sm text-success-800">
              <CheckCircle2 className="size-4" /> Todo listo para cerrar.
            </p>
          ) : (
            <ul className="space-y-2">
              {closure.problems.map((p) => (
                <li key={p.code} className="flex items-start gap-2 text-sm text-danger-700">
                  <AlertTriangle className="mt-0.5 size-4 shrink-0" /> {p.message}
                </li>
              ))}
            </ul>
          )}
          {dirty && <p className="mt-2 text-xs text-fg-muted">Guarda el acta para actualizar la validación.</p>}
          {can && (
            <div className="mt-4 flex flex-col gap-2">
              <Button variant="success" icon={<Lock className="size-4" />} disabled={dirty} onClick={() => setClosing(true)}>
                Cerrar proyecto
              </Button>
              <Button
                variant="secondary"
                loading={m.cancel.isPending}
                onClick={() => confirm('¿Cancelar el proceso de cierre? El proyecto volverá a Activo.') && m.cancel.mutate(undefined, { onError: (e) => toast.error(errorMessage(e)) })}
              >
                Cancelar cierre
              </Button>
            </div>
          )}
        </Card>
      </div>
      {closing && <CloseModal project={project} closure={closure} onClose={() => setClosing(false)} />}
    </div>
  )
}

function CloseModal({ project, closure, onClose }: { project: ProjectDetail; closure: Closure; onClose: () => void }) {
  const { close } = useClosureMutations(project.id)
  const toast = useToast()
  const open = closure.metrics.openTasks
  const [justification, setJustification] = useState('')
  const blocking = closure.problems.filter((p) => p.code !== 'TAREAS_ABIERTAS')
  const valid = blocking.length === 0 && (open === 0 || justification.trim().length >= MIN_JUSTIFICATION)
  return (
    <Modal
      open
      onClose={onClose}
      title="Cerrar proyecto"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button
            variant={open > 0 ? 'danger' : 'success'}
            disabled={!valid}
            loading={close.isPending}
            onClick={() =>
              close.mutate(
                { force: open > 0, justification: open > 0 ? justification : undefined },
                { onSuccess: () => { toast.success('Proyecto cerrado'); onClose() }, onError: (e) => toast.error(errorMessage(e)) },
              )
            }
          >
            {open > 0 ? 'Forzar cierre' : 'Confirmar cierre'}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm">
        {blocking.length > 0 && (
          <div className="rounded-md bg-danger-50 p-3 text-danger-700">
            Resuelve primero: {blocking.map((b) => b.message).join(' ')}
          </div>
        )}
        {open > 0 ? (
          <>
            <div className="flex items-start gap-2 rounded-md bg-danger-50 p-3 text-danger-700">
              <AlertTriangle className="mt-0.5 size-4 shrink-0" />
              <span>
                Quedan <strong>{open}</strong> tarea(s) abierta(s). Puedes forzar el cierre con una justificación; quedará registrada en el acta y en el historial.
              </span>
            </div>
            <Textarea
              label="Justificación del cierre forzado"
              required
              rows={4}
              value={justification}
              onChange={(e) => setJustification(e.target.value)}
              hint={`${justification.trim().length}/${MIN_JUSTIFICATION} caracteres mínimos`}
            />
          </>
        ) : (
          <p>El proyecto pasará a estado <strong>Cerrado</strong> y quedará en solo lectura. Solo un Admin podrá reabrirlo.</p>
        )}
      </div>
    </Modal>
  )
}

function MetricsCard({ closure }: { closure: Closure }) {
  const planned = differenceInCalendarDays(parseISO(closure.plannedEnd), parseISO(closure.plannedStart)) + 1
  const actual =
    closure.actualStart && closure.actualEnd ? differenceInCalendarDays(parseISO(closure.actualEnd), parseISO(closure.actualStart)) + 1 : null
  const deviation = closure.actualEnd ? differenceInCalendarDays(parseISO(closure.actualEnd), parseISO(closure.plannedEnd)) : null
  return (
    <Card title="Planeado vs real">
      <div className="mb-3 flex items-center gap-2">
        <ProgressBar value={closure.metrics.progress} />
        <span className="text-sm font-semibold">{closure.metrics.progress}%</span>
      </div>
      <dl className="space-y-1.5 text-sm">
        <div className="flex justify-between"><dt className="text-fg-muted">Tareas completadas</dt><dd className="font-medium">{closure.metrics.completedTasks}/{closure.metrics.totalTasks}</dd></div>
        <div className="flex justify-between"><dt className="text-fg-muted">Tareas abiertas</dt><dd className={cn('font-medium', closure.metrics.openTasks > 0 && 'text-danger-500')}>{closure.metrics.openTasks}</dd></div>
        <div className="flex justify-between"><dt className="text-fg-muted">Inicio plan / real</dt><dd className="text-right">{fmtDate(closure.plannedStart)} / {fmtDate(closure.actualStart)}</dd></div>
        <div className="flex justify-between"><dt className="text-fg-muted">Fin plan / real</dt><dd className="text-right">{fmtDate(closure.plannedEnd)} / {fmtDate(closure.actualEnd)}</dd></div>
        <div className="flex justify-between"><dt className="text-fg-muted">Duración plan / real</dt><dd>{planned} / {actual ?? '—'} días</dd></div>
        {deviation != null && (
          <div className="flex justify-between">
            <dt className="text-fg-muted">Desviación</dt>
            <dd className={cn('font-medium', deviation > 0 ? 'text-danger-500' : 'text-success-700')}>
              {deviation > 0 ? `+${deviation} días` : deviation === 0 ? 'En fecha' : `${deviation} días`}
            </dd>
          </div>
        )}
      </dl>
    </Card>
  )
}

/** Acta de cierre en modo solo lectura (imprimible). */
function ClosureRecord({ project, closure }: { project: ProjectDetail; closure: Closure }) {
  return (
    <div className="mx-auto max-w-4xl space-y-4 p-4 sm:p-6">
      <div className="flex justify-end print:hidden">
        <Button variant="secondary" icon={<Printer className="size-4" />} onClick={() => window.print()}>
          Imprimir acta
        </Button>
      </div>
      <Card>
        <div className="space-y-1">
          <div className="text-xs text-fg-muted uppercase">Acta de cierre · {project.code}</div>
          <h2 className="text-xl font-semibold">{project.name}</h2>
          <div className="text-sm text-fg-muted">
            Cerrado el {fmtDateTime(closure.closedAt)} por {closure.closedByName ?? '—'}
            {project.clientName && ` · Cliente: ${project.clientName}`}
          </div>
          {closure.isForced && (
            <div className="mt-3 rounded-md bg-danger-50 p-3 text-sm text-danger-700">
              <strong>Cierre forzado</strong> con {closure.metrics.openTasks} tarea(s) abierta(s). Justificación: {closure.forceJustification}
            </div>
          )}
        </div>
      </Card>
      <div className="grid gap-4 md:grid-cols-2">
        <Card title="Resumen">
          <p className="text-sm whitespace-pre-wrap">{closure.summary}</p>
        </Card>
        <MetricsCard closure={closure} />
      </div>
      <Card title="Entregables">
        {closure.deliverables.length === 0 ? (
          <p className="text-sm text-fg-muted">Sin entregables registrados.</p>
        ) : (
          <ul className="space-y-1 text-sm">
            {closure.deliverables.map((d, i) => (
              <li key={i} className="flex items-center gap-2">
                <CheckCircle2 className="size-4 text-success-700" /> {d}
              </li>
            ))}
          </ul>
        )}
      </Card>
      <Card title="Checklist de cierre">
        <ul className="space-y-1 text-sm">
          {closure.checklist.map((i) => (
            <li key={i.id} className="flex items-center gap-2">
              {i.isDone ? <CheckCircle2 className="size-4 text-success-700" /> : <X className="size-4 text-fg-muted" />}
              {i.text}
              {i.doneByName && <span className="text-xs text-fg-muted">— {i.doneByName}</span>}
            </li>
          ))}
        </ul>
      </Card>
      <Card title="Lecciones aprendidas">
        {closure.lessons.length === 0 ? (
          <p className="text-sm text-fg-muted">Sin lecciones registradas.</p>
        ) : (
          <div className="space-y-3">
            {closure.lessons.map((l, i) => (
              <div key={i} className="rounded-md border border-border p-3 text-sm">
                <Badge tone="primary">{l.category}</Badge>
                <dl className="mt-2 grid gap-2 sm:grid-cols-3">
                  <div><dt className="text-xs text-fg-muted">Qué funcionó bien</dt><dd>{l.whatWentWell || '—'}</dd></div>
                  <div><dt className="text-xs text-fg-muted">Qué mejorar</dt><dd>{l.whatToImprove || '—'}</dd></div>
                  <div><dt className="text-xs text-fg-muted">Recomendación</dt><dd>{l.recommendation || '—'}</dd></div>
                </dl>
              </div>
            ))}
          </div>
        )}
      </Card>
    </div>
  )
}
