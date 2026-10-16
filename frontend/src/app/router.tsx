import { lazy } from 'react'
import { createBrowserRouter } from 'react-router-dom'
import { MainLayout, RequireRole } from '@/components/layout/MainLayout'
import { LoginPage } from '@/features/auth/LoginPage'

// Las páginas se cargan bajo demanda (bundle inicial más liviano, importante en móviles/PWA).
const DashboardPage = lazy(() => import('@/features/dashboard/DashboardPage').then((m) => ({ default: m.DashboardPage })))
const UsersPage = lazy(() => import('@/features/admin/UsersPage').then((m) => ({ default: m.UsersPage })))
const TemplatesPage = lazy(() => import('@/features/admin/TemplatesPage').then((m) => ({ default: m.TemplatesPage })))
const ClosureChecklistPage = lazy(() => import('@/features/admin/ClosureChecklistPage').then((m) => ({ default: m.ClosureChecklistPage })))
const DesignSystemPage = lazy(() => import('@/features/design/DesignSystemPage').then((m) => ({ default: m.DesignSystemPage })))
const ProjectsPage = lazy(() => import('@/features/projects/ProjectsPage').then((m) => ({ default: m.ProjectsPage })))
const NewProjectPage = lazy(() => import('@/features/projects/NewProjectPage').then((m) => ({ default: m.NewProjectPage })))
const ProjectPage = lazy(() => import('@/features/projects/ProjectPage').then((m) => ({ default: m.ProjectPage })))
const MyTasksPage = lazy(() => import('@/features/tasks/MyTasksPage').then((m) => ({ default: m.MyTasksPage })))

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    element: <MainLayout />,
    children: [
      { index: true, element: <DashboardPage /> },
      { path: 'my-tasks', element: <MyTasksPage /> },
      { path: 'projects', element: <ProjectsPage /> },
      {
        path: 'projects/new',
        element: (
          <RequireRole roles={['Admin', 'Gestor']}>
            <NewProjectPage />
          </RequireRole>
        ),
      },
      { path: 'projects/:projectId/:tab?', element: <ProjectPage /> },
      {
        path: 'admin/users',
        element: (
          <RequireRole roles={['Admin']}>
            <UsersPage />
          </RequireRole>
        ),
      },
      {
        path: 'admin/templates',
        element: (
          <RequireRole roles={['Admin', 'Gestor']}>
            <TemplatesPage />
          </RequireRole>
        ),
      },
      {
        path: 'admin/closure-checklist',
        element: (
          <RequireRole roles={['Admin']}>
            <ClosureChecklistPage />
          </RequireRole>
        ),
      },
      {
        path: 'admin/design',
        element: (
          <RequireRole roles={['Admin']}>
            <DesignSystemPage />
          </RequireRole>
        ),
      },
      { path: '*', element: <DashboardPage /> },
    ],
  },
])
