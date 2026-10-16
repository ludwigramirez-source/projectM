import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { AlertTriangle, FolderKanban, Plus, Search } from 'lucide-react'
import { useProjects, useSpaces } from '@/api/projects'
import { useAuth } from '@/app/auth'
import {
  Button,
  Card,
  EmptyState,
  HealthBadge,
  Input,
  PageHeader,
  PageLoader,
  Pagination,
  ProgressBar,
  ProjectStatusBadge,
  Select,
} from '@/components/ui'
import { fmtShortDate } from '@/lib/format'
import { projectStatusMeta, type ProjectStatus } from '@/lib/status'

export function ProjectsPage() {
  const [q, setQ] = useState('')
  const [status, setStatus] = useState('')
  const [spaceId, setSpaceId] = useState('')
  const [page, setPage] = useState(1)
  const { data, isLoading } = useProjects({ q, status, spaceId, page })
  const { data: spaces } = useSpaces()
  const { hasRole } = useAuth()
  const navigate = useNavigate()
  const reset = <T,>(fn: (v: T) => void) => (v: T) => {
    fn(v)
    setPage(1)
  }

  return (
    <div>
      <PageHeader
        title="Proyectos"
        subtitle="Todos los proyectos en los que participas"
        actions={
          hasRole('Admin', 'Gestor') && (
            <Button icon={<Plus className="size-4" />} onClick={() => navigate('/projects/new')}>
              Nuevo proyecto
            </Button>
          )
        }
      />
      <div className="space-y-4 p-4 sm:p-6">
        <div className="flex flex-wrap gap-2">
          <div className="relative min-w-56 flex-1">
            <Search className="pointer-events-none absolute top-2.5 left-2.5 size-4 text-fg-muted" />
            <Input placeholder="Buscar por nombre, código o cliente" className="pl-8" value={q} onChange={(e) => reset(setQ)(e.target.value)} aria-label="Buscar proyectos" />
          </div>
          <Select value={spaceId} onChange={(e) => reset(setSpaceId)(e.target.value)} aria-label="Espacio" wrapperClassName="w-44">
            <option value="">Todos los espacios</option>
            {spaces?.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </Select>
          <Select value={status} onChange={(e) => reset(setStatus)(e.target.value)} aria-label="Estado" wrapperClassName="w-40">
            <option value="">Todos los estados</option>
            {(Object.keys(projectStatusMeta) as ProjectStatus[]).map((s) => (
              <option key={s} value={s}>
                {projectStatusMeta[s].label}
              </option>
            ))}
          </Select>
        </div>

        {isLoading ? (
          <PageLoader />
        ) : !data?.items.length ? (
          <Card>
            <EmptyState
              icon={<FolderKanban className="size-8" />}
              title="No hay proyectos"
              description="Cuando abras un proyecto o te agreguen a uno, aparecerá aquí."
            />
          </Card>
        ) : (
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {data.items.map((p) => (
              <Link
                key={p.id}
                to={`/projects/${p.id}`}
                className="group rounded-lg border border-border bg-surface p-4 transition-shadow hover:border-border-strong hover:shadow-md"
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="text-xs text-fg-muted">
                      {p.code} · {p.spaceName}
                    </div>
                    <h3 className="truncate font-semibold group-hover:text-primary-fg">{p.name}</h3>
                    {p.clientName && <div className="truncate text-xs text-fg-muted">{p.clientName}</div>}
                  </div>
                  <ProjectStatusBadge status={p.status} />
                </div>
                <div className="mt-4 flex items-center gap-2">
                  <ProgressBar value={p.progress} />
                  <span className="w-10 text-right text-xs font-medium">{p.progress}%</span>
                </div>
                <div className="mt-3 flex flex-wrap items-center justify-between gap-2 text-xs text-fg-muted">
                  <span>
                    {fmtShortDate(p.plannedStart)} – {fmtShortDate(p.plannedEnd)}
                  </span>
                  <span>
                    {p.completedTasks}/{p.totalTasks} tareas
                  </span>
                </div>
                <div className="mt-3 flex items-center justify-between gap-2">
                  <span className="truncate text-xs">Gestor: {p.managerName}</span>
                  <div className="flex items-center gap-1.5">
                    {p.overdueTasks > 0 && p.status !== 'Cerrado' && (
                      <span className="inline-flex items-center gap-1 text-xs font-medium text-danger-fg">
                        <AlertTriangle className="size-3.5" />
                        {p.overdueTasks}
                      </span>
                    )}
                    {p.status !== 'Cerrado' && <HealthBadge health={p.health} />}
                  </div>
                </div>
              </Link>
            ))}
          </div>
        )}
        {data && <Pagination page={data.page} pageSize={data.pageSize} total={data.totalCount} onChange={setPage} />}
      </div>
    </div>
  )
}
