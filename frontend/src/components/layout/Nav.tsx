import type { ReactNode } from 'react'
import { NavLink } from 'react-router-dom'
import { cn } from '@/lib/cn'

export function NavItem({ to, icon, children, end }: { to: string; icon?: ReactNode; children: ReactNode; end?: boolean }) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        cn(
          'flex items-center gap-2.5 rounded-md px-3 py-1.5 text-sm transition-colors',
          isActive ? 'bg-sidebar-active font-medium text-sidebar-fg' : 'text-sidebar-fg-muted hover:bg-sidebar-active/60 hover:text-sidebar-fg',
        )
      }
    >
      {icon}
      <span className="truncate">{children}</span>
    </NavLink>
  )
}

export function NavSection({ title, children, action }: { title: string; children: ReactNode; action?: ReactNode }) {
  return (
    <div className="mt-4">
      <div className="mb-1 flex items-center justify-between px-3 text-[11px] font-semibold tracking-wider text-sidebar-fg-muted uppercase">
        {title}
        {action}
      </div>
      <div className="space-y-0.5">{children}</div>
    </div>
  )
}
