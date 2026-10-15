import { useState } from 'react'
import { Plus } from 'lucide-react'
import {
  Avatar,
  Badge,
  Button,
  Card,
  Checkbox,
  Input,
  Modal,
  PageHeader,
  PriorityBadge,
  ProgressBar,
  ProjectStatusBadge,
  Select,
  StatusBadge,
  useToast,
} from '@/components/ui'
import { PRIORITIES, TASK_STATUSES, type ProjectStatus } from '@/lib/status'

const SCALES = ['primary', 'secondary', 'accent', 'success', 'danger', 'neutral'] as const
const STEPS = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900] as const

/** Catálogo visual del sistema de diseño (solo Admin; útil para revisar tokens y contraste). */
export function DesignSystemPage() {
  const [modal, setModal] = useState(false)
  const toast = useToast()
  return (
    <div>
      <PageHeader title="Sistema de diseño" subtitle="Tokens de la paleta y componentes base" />
      <div className="space-y-6 p-4 sm:p-6">
        <Card title="Paleta">
          <div className="space-y-2">
            {SCALES.map((s) => (
              <div key={s} className="flex items-center gap-2">
                <span className="w-20 text-xs font-medium">{s}</span>
                <div className="grid flex-1 grid-cols-10 overflow-hidden rounded-md">
                  {STEPS.map((n) => (
                    <div
                      key={n}
                      className="h-8"
                      style={{ background: `rgb(var(--pm-${s}-${n}))` }}
                      title={`${s}-${n}`}
                    />
                  ))}
                </div>
              </div>
            ))}
          </div>
        </Card>
        <Card title="Botones">
          <div className="flex flex-wrap gap-2">
            <Button icon={<Plus className="size-4" />}>Primario</Button>
            <Button variant="secondary">Secundario</Button>
            <Button variant="ghost">Fantasma</Button>
            <Button variant="success">Completar</Button>
            <Button variant="danger">Eliminar</Button>
            <Button loading>Guardando</Button>
            <Button onClick={() => setModal(true)} variant="secondary">
              Abrir modal
            </Button>
            <Button variant="secondary" onClick={() => toast.success('Cambios guardados')}>
              Toast
            </Button>
          </div>
        </Card>
        <Card title="Estados y badges">
          <div className="flex flex-wrap gap-2">
            {TASK_STATUSES.map((s) => (
              <StatusBadge key={s} status={s} />
            ))}
            <StatusBadge status="EnProgreso" overdue />
            {PRIORITIES.map((p) => (
              <PriorityBadge key={p} priority={p} />
            ))}
            {(['Planeacion', 'Activo', 'EnPausa', 'EnCierre', 'Cerrado'] as ProjectStatus[]).map((s) => (
              <ProjectStatusBadge key={s} status={s} />
            ))}
            <Badge tone="accent">Nuevo</Badge>
          </div>
          <div className="mt-4 flex items-center gap-2">
            <Avatar name="Ana Gómez" />
            <Avatar name="Luis Pérez" />
            <Avatar name="María Torres" size="sm" />
            <Avatar />
          </div>
          <div className="mt-4 max-w-sm">
            <ProgressBar value={64} />
          </div>
        </Card>
        <Card title="Formularios">
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Nombre" placeholder="Escribe…" required />
            <Input label="Con error" error="Este campo es obligatorio" />
            <Select label="Prioridad">
              {PRIORITIES.map((p) => (
                <option key={p}>{p}</option>
              ))}
            </Select>
            <Checkbox label="Acepto" />
          </div>
        </Card>
      </div>
      <Modal
        open={modal}
        onClose={() => setModal(false)}
        title="Ejemplo de modal"
        footer={
          <>
            <Button variant="secondary" onClick={() => setModal(false)}>
              Cancelar
            </Button>
            <Button onClick={() => setModal(false)}>Aceptar</Button>
          </>
        }
      >
        Contenido del modal.
      </Modal>
    </div>
  )
}
