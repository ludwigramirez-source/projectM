import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { CheckSquare, ChevronDown, ClipboardCheck, FolderKanban, LayoutDashboard, LayoutTemplate, LogOut, Palette, ShieldCheck, Users } from 'lucide-react'
import { NavItem, NavSection } from './Nav'
import { SpaceTree } from './SpaceTree'
import { TaskDrawer } from '@/features/tasks/TaskDrawer'
import { AppShell, Logo } from './AppShell'
import { Avatar, PageLoader } from '@/components/ui'
import { roleLabel, useAuth } from '@/app/auth'

function Sidebar() {
  const { hasRole } = useAuth()
  return (
    <>
      <Logo />
      <nav className="flex-1 overflow-y-auto px-2 pb-4">
        <div className="space-y-0.5">
          <NavItem to="/" end icon={<LayoutDashboard className="size-4" />}>
            Inicio
          </NavItem>
          <NavItem to="/my-tasks" icon={<CheckSquare className="size-4" />}>
            Mis tareas
          </NavItem>
          <NavItem to="/projects" end icon={<FolderKanban className="size-4" />}>
            Proyectos
          </NavItem>
        </div>
        <SpaceTree />
        {hasRole('Admin', 'Gestor') && (
          <NavSection title="Administración">
            {hasRole('Admin') && (
              <NavItem to="/admin/users" icon={<Users className="size-4" />}>
                Usuarios
              </NavItem>
            )}
            <NavItem to="/admin/templates" icon={<LayoutTemplate className="size-4" />}>
              Plantillas
            </NavItem>
            {hasRole('Admin') && (
              <>
                <NavItem to="/admin/closure-checklist" icon={<ClipboardCheck className="size-4" />}>
                  Checklist de cierre
                </NavItem>
                <NavItem to="/admin/design" icon={<Palette className="size-4" />}>
                  Sistema de diseño
                </NavItem>
              </>
            )}
          </NavSection>
        )}
      </nav>
    </>
  )
}

function UserMenu() {
  const { user, logout } = useAuth()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const h = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && setOpen(false)
    document.addEventListener('mousedown', h)
    return () => document.removeEventListener('mousedown', h)
  }, [])
  if (!user) return null
  return (
    <div className="relative" ref={ref}>
      <button
        onClick={() => setOpen((o) => !o)}
        className="flex items-center gap-2 rounded-md p-1 hover:bg-surface-muted"
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <Avatar name={user.fullName} size="sm" />
        <span className="hidden text-sm font-medium sm:inline">{user.fullName}</span>
        <ChevronDown className="size-4 text-fg-muted" />
      </button>
      {open && (
        <div role="menu" className="absolute right-0 z-30 mt-1 w-56 rounded-md border border-border bg-surface py-1 shadow-lg">
          <div className="border-b border-border px-3 py-2">
            <div className="truncate text-sm font-medium">{user.fullName}</div>
            <div className="truncate text-xs text-fg-muted">{user.email}</div>
            <div className="mt-1 flex items-center gap-1 text-xs text-fg-muted">
              <ShieldCheck className="size-3.5" /> {roleLabel[user.role]}
            </div>
          </div>
          <button role="menuitem" onClick={logout} className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-surface-muted">
            <LogOut className="size-4" /> Cerrar sesión
          </button>
        </div>
      )}
    </div>
  )
}

export function MainLayout() {
  const { user, loading } = useAuth()
  const location = useLocation()
  if (loading) return <PageLoader />
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  return (
    <AppShell
      sidebar={<Sidebar />}
      topbar={
        <div className="flex flex-1 items-center justify-end gap-2">
          <UserMenu />
        </div>
      }
    >
      <Outlet />
      <TaskDrawer />
    </AppShell>
  )
}

/** Restringe una ruta a ciertos roles. */
export function RequireRole({ roles, children }: { roles: ('Admin' | 'Gestor' | 'Miembro' | 'Observador')[]; children: ReactNode }) {
  const { hasRole } = useAuth()
  return hasRole(...roles) ? <>{children}</> : <Navigate to="/" replace />
}
